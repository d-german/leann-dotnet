using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using CSharpFunctionalExtensions;
using LeannMcp.Models;
using Microsoft.Extensions.Logging;

namespace LeannMcp.Services;

/// <summary>
/// Discovers, lazily loads, and caches LEANN indexes.
/// Mirrors Python mcp.py _SEARCHER_CACHE + _get_searcher + _list_indexes + _onboard.
/// </summary>
public sealed class IndexManager
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<IndexManager> _logger;
    private readonly ConcurrentDictionary<string, LeannIndex> _cache = new();
    private readonly string _indexesDir;

    public string IndexesDir => _indexesDir;
    private readonly int _dimensions;

    public IndexManager(IEmbeddingService embeddingService, ILogger<IndexManager> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
        _indexesDir = Path.Combine(Directory.GetCurrentDirectory(), ".leann", "indexes");
        _dimensions = 768;
    }

    public Result<IReadOnlyList<SearchResult>> Search(string indexName, string query, int topK = 5, int complexity = 32)
    {
        return GetOrLoadIndex(indexName)
            .Bind(index => ExecuteSearch(index, query, topK));
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
            _embeddingService.Warmup();
            GetOrLoadIndex(warmupIndex);
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
            var indexDir = Path.Combine(_indexesDir, indexName);
            var metaPath = Path.Combine(indexDir, "documents.leann.meta.json");

            if (!File.Exists(metaPath))
                return Result.Failure<LeannIndex>($"Index '{indexName}' not found");

            var metaJson = File.ReadAllText(metaPath);
            var metadata = JsonSerializer.Deserialize<IndexMetadata>(metaJson);
            if (metadata is null)
                return Result.Failure<LeannIndex>($"Failed to deserialize metadata for '{indexName}'");

            // Resolve passage file path (relative to index directory)
            var passageSource = metadata.PassageSources.FirstOrDefault();
            if (passageSource is null)
                return Result.Failure<LeannIndex>($"No passage sources in '{indexName}'");

            var passagePath = ResolvePassagePath(indexDir, passageSource);
            if (!File.Exists(passagePath))
                return Result.Failure<LeannIndex>($"Passage file not found: {passagePath}");

            var passageStore = new JsonlPassageStore(passagePath);
            _logger.LogInformation("Loaded {Count} passages for '{Name}'", passageStore.Count, indexName);

            // Load pre-computed embeddings
            var embeddingsPath = Path.Combine(indexDir, "documents.embeddings.bin");
            var idsPath = Path.Combine(indexDir, "documents.ids.txt");

            if (!File.Exists(embeddingsPath) || !File.Exists(idsPath))
                return Result.Failure<LeannIndex>(
                    $"Pre-computed embeddings not found for '{indexName}'. " +
                    "Run build-dotnet-indexes.py first.");

            var vectorIndex = new FlatVectorIndex(embeddingsPath, idsPath, _dimensions);
            _logger.LogInformation("Loaded {Count} embeddings for '{Name}'", vectorIndex.Count, indexName);

            return Result.Success(new LeannIndex(metadata, passageStore, vectorIndex));
        }
        catch (Exception ex)
        {
            return Result.Failure<LeannIndex>($"Error loading index '{indexName}': {ex.Message}");
        }
    }

    private Result<IReadOnlyList<SearchResult>> ExecuteSearch(LeannIndex index, string query, int topK)
    {
        return _embeddingService.ComputeEmbedding(query)
            .Bind(embedding => index.VectorIndex.Search(embedding, topK))
            .Bind(hits => EnrichResults(index.PassageStore, hits));
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
        if (!Directory.Exists(_indexesDir))
            return [];

        return Directory.GetDirectories(_indexesDir)
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
        IVectorIndex VectorIndex);
}
