using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using CSharpFunctionalExtensions;
using LeannMcp.Models;
using LeannMcp.Services.Search;
using LeannMcp.Services.Workspace;
using Microsoft.Extensions.Logging;

namespace LeannMcp.Services;

/// <summary>
/// Discovers, lazily loads, and caches LEANN indexes.
/// Mirrors Python mcp.py _SEARCHER_CACHE + _get_searcher + _list_indexes + _onboard.
/// </summary>
public sealed class IndexManager
{
    private readonly IEmbeddingServiceFactory _factory;
    private readonly ILogger<IndexManager> _logger;
    private readonly ConcurrentDictionary<string, LeannIndex> _cache = new();
    private readonly WorkspaceResolver? _resolver;
    private readonly string? _staticIndexesDir;
    private string? _lastResolvedIndexesDir;
    private readonly object _invalidationLock = new();

    public string IndexesDir => CurrentIndexesDir();

    public IndexManager(
        IEmbeddingServiceFactory factory,
        ILogger<IndexManager> logger,
        string? indexesDir = null)
    {
        _factory = factory;
        _logger = logger;
        _staticIndexesDir = indexesDir ?? Path.Combine(
            Environment.GetEnvironmentVariable("LEANN_DATA_ROOT") ?? Directory.GetCurrentDirectory(),
            ".leann", "indexes");
    }

    /// <summary>
    /// MCP-server-mode constructor: resolves the indexes directory per call via
    /// <see cref="WorkspaceResolver"/> and clears the cache when it changes.
    /// </summary>
    public IndexManager(
        IEmbeddingServiceFactory factory,
        ILogger<IndexManager> logger,
        WorkspaceResolver resolver)
    {
        _factory = factory;
        _logger = logger;
        _resolver = resolver;
    }

    private string CurrentIndexesDir()
    {
        if (_resolver is null) return _staticIndexesDir!;

        var resolved = _resolver.ResolveIndexesDir();
        if (!string.Equals(resolved, _lastResolvedIndexesDir, StringComparison.OrdinalIgnoreCase))
        {
            lock (_invalidationLock)
            {
                if (!string.Equals(resolved, _lastResolvedIndexesDir, StringComparison.OrdinalIgnoreCase))
                {
                    if (_lastResolvedIndexesDir is not null)
                        _logger.LogInformation("Indexes directory changed: {Old} -> {New}; clearing cache.", _lastResolvedIndexesDir, resolved);
                    _cache.Clear();
                    _lastResolvedIndexesDir = resolved;
                }
            }
        }
        return resolved;
    }

    public Result<IReadOnlyList<SearchResult>> Search(
        string indexName,
        string query,
        int topK = 5,
        int complexity = 32,
        double dedupThreshold = NearDuplicateFilter.DefaultThreshold,
        int dedupOverFetchFactor = NearDuplicateFilter.DefaultOverFetchFactor)
    {
        return GetOrLoadIndex(indexName)
            .Bind(index => ExecuteSearch(index, query, topK, dedupThreshold, dedupOverFetchFactor));
    }

    public Result<IReadOnlyList<string>> ListIndexes()
    {
        try
        {
            var indexes = DiscoverIndexNames();
            return Result.Success<IReadOnlyList<string>>(indexes);
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<string>>($"Error listing indexes: {ex.Message}");
        }
    }

    public Result<string> Warmup()
    {
        var indexes = DiscoverIndexNames();
        if (indexes.Count == 0)
            return Result.Failure<string>("Onboard failed: No indexes found.");

        var lines = new List<string> { $"Found {indexes.Count} indexes." };
        var warmupIndex = indexes[0];
        var sw = Stopwatch.StartNew();

        try
        {
            // Load the warmup index first so we know which model to warm up.
            var loadResult = GetOrLoadIndex(warmupIndex);
            if (loadResult.IsSuccess)
                loadResult.Value.EmbeddingService.Warmup();
            sw.Stop();
            lines.Add($"Model loaded and warmed up on '{warmupIndex}' in {sw.Elapsed.TotalSeconds:F1}s.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            lines.Add($"Warmup failed after {sw.Elapsed.TotalSeconds:F1}s: {ex.Message}");
        }

        lines.Add("");
        lines.Add("Available indexes:");
        foreach (var idx in indexes)
        {
            var cached = _cache.ContainsKey(idx) ? "(warm)" : "";
            lines.Add($"  - {idx} {cached}");
        }

        lines.Add("");
        lines.Add("Onboard complete. All subsequent searches will be fast.");
        return Result.Success(string.Join("\n", lines));
    }

    private Result<LeannIndex> GetOrLoadIndex(string indexName)
    {
        if (_cache.TryGetValue(indexName, out var cached))
            return Result.Success(cached);

        return LoadIndex(indexName)
            .Tap(index => _cache.TryAdd(indexName, index))
            .TapError(error => _logger.LogError("Failed to load index '{Name}': {Error}", indexName, error));
    }

    private Result<LeannIndex> LoadIndex(string indexName)
    {
        try
        {
            var indexDir = Path.Combine(CurrentIndexesDir(), indexName);
            var metaPath = Path.Combine(indexDir, "documents.leann.meta.json");

            if (!File.Exists(metaPath))
                return Result.Failure<LeannIndex>($"Index '{indexName}' not found");

            var metaJson = File.ReadAllText(metaPath);
            var metadata = JsonSerializer.Deserialize<IndexMetadata>(metaJson);
            if (metadata is null)
                return Result.Failure<LeannIndex>($"Failed to deserialize metadata for '{indexName}'");

            var descriptorResult = ModelRegistry.GetById(metadata.EmbeddingModel)
                .ToResult(
                    $"Index '{indexName}' was built with model '{metadata.EmbeddingModel}', " +
                    "which is not registered in ModelRegistry. " +
                    "Add it to ModelRegistry.cs and re-setup, or rebuild this index against a registered model.");
            if (descriptorResult.IsFailure)
                return Result.Failure<LeannIndex>(descriptorResult.Error);
            var descriptor = descriptorResult.Value;

            var compatibility = IndexCompatibility.EnsureManifestIntegrity(metadata, descriptor, indexDir);
            if (compatibility.IsFailure)
                return Result.Failure<LeannIndex>(compatibility.Error);

            var serviceResult = _factory.GetOrCreate(descriptor);
            if (serviceResult.IsFailure)
                return Result.Failure<LeannIndex>(serviceResult.Error);
            var embeddingService = serviceResult.Value;

            // Resolve passage file path (relative to index directory)
            var passageSource = metadata.PassageSources.FirstOrDefault();
            if (passageSource is null)
                return Result.Failure<LeannIndex>($"No passage sources in '{indexName}'");

            var passagePath = ResolvePassagePath(indexDir, passageSource);
            if (!File.Exists(passagePath))
                return Result.Failure<LeannIndex>($"Passage file not found: {passagePath}");

            var passageStore = new JsonlPassageStore(passagePath);
            _logger.LogInformation("Loaded {Count} passages for '{Name}'", passageStore.Count, indexName);

            // Load pre-computed embeddings (per-index dimensions from manifest)
            var embeddingsPath = Path.Combine(indexDir, "documents.embeddings.bin");
            var idsPath = Path.Combine(indexDir, "documents.ids.txt");

            if (!File.Exists(embeddingsPath) || !File.Exists(idsPath))
                return Result.Failure<LeannIndex>(
                    $"Pre-computed embeddings not found for '{indexName}'. " +
                    "Run build-dotnet-indexes.py first.");

            var vectorIndex = new FlatVectorIndex(embeddingsPath, idsPath, descriptor.Dimensions);
            _logger.LogInformation("Loaded {Count} embeddings for '{Name}' (model: {Model}, dim: {Dim})",
                vectorIndex.Count, indexName, descriptor.Id, descriptor.Dimensions);

            var bm25 = new BM25Index(passageStore.EnumerateAll());
            _logger.LogInformation("Built BM25 index for '{Name}' over {Count} passages", indexName, bm25.Count);

            return Result.Success(new LeannIndex(metadata, passageStore, vectorIndex, descriptor, embeddingService, bm25));
        }
        catch (Exception ex)
        {
            return Result.Failure<LeannIndex>($"Error loading index '{indexName}': {ex.Message}");
        }
    }

    private Result<IReadOnlyList<SearchResult>> ExecuteSearch(
        LeannIndex index,
        string query,
        int topK,
        double dedupThreshold,
        int dedupOverFetchFactor)
    {
        var fetchK = ComputeFetchK(topK, dedupThreshold, dedupOverFetchFactor);
        return index.EmbeddingService.ComputeEmbedding(query)
            .Bind(embedding => index.VectorIndex.Search(embedding, fetchK))
            .Map(denseHits => FuseWithBm25(index.BM25, query, denseHits, fetchK))
            .Map(fusedHits => NearDuplicateFilter.Filter(fusedHits, index.VectorIndex.TryGetEmbedding, topK, dedupThreshold))
            .Bind(filteredHits => EnrichResults(index.PassageStore, filteredHits));
    }

    private static IReadOnlyList<(string Id, float Score)> FuseWithBm25(
        BM25Index bm25,
        string query,
        IReadOnlyList<(string Id, float Score)> denseHits,
        int fetchK)
    {
        var lexicalHits = bm25.Search(query, fetchK);
        var pin = bm25.FindBestIdentifierMatch(query);
        return pin.HasValue
            ? PinAndFuse(pin.Value, denseHits, lexicalHits, fetchK)
            : ReciprocalRankFusion.Fuse(denseHits, lexicalHits, fetchK, lexicalWeight: 2.0f);
    }

    private static IReadOnlyList<(string Id, float Score)> PinAndFuse(
        string pinnedId,
        IReadOnlyList<(string Id, float Score)> denseHits,
        IReadOnlyList<(string Id, float Score)> lexicalHits,
        int fetchK)
    {
        var filteredDense = denseHits.Where(h => h.Id != pinnedId).ToArray();
        var filteredLex = lexicalHits.Where(h => h.Id != pinnedId).ToArray();
        var remaining = ReciprocalRankFusion.Fuse(filteredDense, filteredLex, Math.Max(0, fetchK - 1), lexicalWeight: 2.0f);

        // Use a value just above the top remaining hit so the pinned doc
        // displays in the same numeric scale as the rest. Ordering is already
        // guaranteed by list position (prepend); the score is purely cosmetic
        // for client UIs/logs/metrics. Avoids leaking a sentinel like
        // float.MaxValue (~3.4e38) into user-visible output.
        var pinnedScore = remaining.Count > 0 ? remaining[0].Score + 0.001f : 1.0f;

        var fused = new List<(string Id, float Score)>(remaining.Count + 1)
        {
            (pinnedId, pinnedScore),
        };
        fused.AddRange(remaining);
        return fused;
    }

    private static int ComputeFetchK(int topK, double dedupThreshold, int overFetchFactor)
    {
        if (dedupThreshold <= 0.0 || dedupThreshold >= 1.0) return topK;
        var factor = Math.Max(1, overFetchFactor);
        return checked(topK * factor);
    }

    private static Result<IReadOnlyList<SearchResult>> EnrichResults(
        IPassageStore store,
        IReadOnlyList<(string Id, float Score)> hits)
    {
        var results = new List<SearchResult>(hits.Count);
        foreach (var (id, score) in hits)
        {
            var passageResult = store.GetPassage(id);
            if (passageResult.IsSuccess)
            {
                var p = passageResult.Value;
                results.Add(new SearchResult(p.Id, score, p.Text, p.Metadata));
            }
        }
        return Result.Success<IReadOnlyList<SearchResult>>(results);
    }

    public List<string> DiscoverIndexNames()
    {
        var indexesDir = CurrentIndexesDir();
        if (!Directory.Exists(indexesDir))
            return [];

        return Directory.GetDirectories(indexesDir)
            .Where(d => File.Exists(Path.Combine(d, "documents.leann.meta.json")))
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Cast<string>()
            .OrderBy(n => n)
            .ToList();
    }

    public static string ResolvePassagePath(string indexDir, PassageSource source)
    {
        // Try relative path first (most common)
        if (!string.IsNullOrEmpty(source.PathRelative))
        {
            var resolved = Path.Combine(indexDir, source.PathRelative);
            if (File.Exists(resolved)) return resolved;
        }

        // Try the direct path
        if (!string.IsNullOrEmpty(source.Path))
        {
            var resolved = Path.Combine(indexDir, source.Path);
            if (File.Exists(resolved)) return resolved;

            // Maybe it is absolute
            if (Path.IsPathRooted(source.Path) && File.Exists(source.Path))
                return source.Path;
        }

        // Fallback to conventional name
        return Path.Combine(indexDir, "documents.leann.passages.jsonl");
    }

    private sealed record LeannIndex(
        IndexMetadata Metadata,
        IPassageStore PassageStore,
        IVectorIndex VectorIndex,
        EmbeddingModelDescriptor Model,
        IEmbeddingService EmbeddingService,
        BM25Index BM25);
}
