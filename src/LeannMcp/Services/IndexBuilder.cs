using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using LeannMcp.Infrastructure;
using LeannMcp.Models;
using Microsoft.Extensions.Logging;

namespace LeannMcp.Services;

/// <summary>
/// Pre-computes passage embeddings for LEANN indexes using the ONNX embedding service.
/// Produces binary files compatible with <see cref="FlatVectorIndex"/>.
/// </summary>
public sealed class IndexBuilder(IEmbeddingService embeddingService, ILogger<IndexBuilder> logger)
{
    private const int DefaultDimensions = 768;

    public Result BuildAll(string indexesDir, int batchSize = 32, bool force = false, string? singleIndex = null, IReadOnlySet<string>? excludeIndexes = null)
    {
        if (!Directory.Exists(indexesDir))
            return Result.Failure($"Indexes directory not found: {indexesDir}");

        var indexes = DiscoverBuildableIndexes(indexesDir);
        if (indexes.Count == 0)
            return Result.Failure("No indexes found with meta.json + passages.jsonl");

        if (singleIndex is not null)
        {
            indexes = indexes.Where(i => i.Name.Equals(singleIndex, StringComparison.OrdinalIgnoreCase)).ToList();
            if (indexes.Count == 0)
                return Result.Failure($"Index '{singleIndex}' not found");
        }

        if (excludeIndexes is { Count: > 0 })
        {
            var before = indexes.Count;
            indexes = indexes.Where(i => !excludeIndexes.Contains(i.Name)).ToList();
            logger.LogInformation("Excluded {Count} index(es): {Names}", before - indexes.Count,
                string.Join(", ", excludeIndexes));
        }

        logger.LogInformation("Found {Count} index(es) to process", indexes.Count);

        var totalSw = Stopwatch.StartNew();
        int succeeded = 0, skipped = 0, failed = 0;

        foreach (var index in indexes)
        {
            var result = BuildIndex(index.Dir, index.Name, batchSize, force);
            if (result.IsSuccess)
            {
                if (result.Value) succeeded++; else skipped++;
            }
            else
            {
                failed++;
                logger.LogError("Failed to build '{Name}': {Error}", index.Name, result.Error);
            }
        }

        totalSw.Stop();
        logger.LogInformation(
            "Build complete in {Elapsed:F1}s — {Succeeded} built, {Skipped} skipped, {Failed} failed",
            totalSw.Elapsed.TotalSeconds, succeeded, skipped, failed);

        return failed > 0
            ? Result.Failure($"{failed} index(es) failed to build")
            : Result.Success();
    }

    /// <returns>true if built, false if skipped</returns>
    public Result<bool> BuildIndex(string indexDir, string indexName, int batchSize, bool force)
    {
        var embeddingsPath = Path.Combine(indexDir, "documents.embeddings.bin");

        if (!force && File.Exists(embeddingsPath))
        {
            logger.LogInformation("[{Name}] Embeddings already exist, skipping (use --force to rebuild)", indexName);
            return Result.Success(false);
        }

        return LoadPassageTextsAndIds(indexDir, indexName)
            .Bind(data => ComputeAllEmbeddings(data.Texts, data.Ids, indexName, batchSize))
            .Bind(embeddings => NormalizeEmbeddings(embeddings, indexName))
            .Bind(embeddings => WriteOutputFiles(embeddings, indexDir, indexName).Map(() => true));
    }

    private Result<(string[] Texts, string[] Ids)> LoadPassageTextsAndIds(string indexDir, string indexName)
    {
        try
        {
            var metaPath = Path.Combine(indexDir, "documents.leann.meta.json");
            if (!File.Exists(metaPath))
                return Result.Failure<(string[], string[])>($"No meta.json in {indexDir}");

            var metadata = JsonSerializer.Deserialize<IndexMetadata>(File.ReadAllText(metaPath));
            if (metadata is null)
                return Result.Failure<(string[], string[])>("Failed to parse meta.json");

            var passageSource = metadata.PassageSources.FirstOrDefault();
            if (passageSource is null)
                return Result.Failure<(string[], string[])>("No passage sources in meta.json");

            var passagePath = IndexManager.ResolvePassagePath(indexDir, passageSource);
            if (!File.Exists(passagePath))
                return Result.Failure<(string[], string[])>($"Passage file not found: {passagePath}");

            var store = new JsonlPassageStore(passagePath);
            logger.LogInformation("[{Name}] Loaded {Count} passages", indexName, store.Count);

            // Load IDs from ids.txt to ensure consistent ordering
            var idsPath = Path.Combine(indexDir, "documents.ids.txt");
            string[] ids;
            if (File.Exists(idsPath))
            {
                ids = File.ReadAllLines(idsPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            }
            else
            {
                // Fall back to passage store ordering
                ids = Enumerable.Range(0, store.Count).Select(i => i.ToString()).ToArray();
            }

            var texts = new string[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                var result = store.GetPassage(ids[i]);
                texts[i] = result.IsSuccess ? result.Value.Text : "";
            }

            return Result.Success((texts, ids));
        }
        catch (Exception ex)
        {
            return Result.Failure<(string[], string[])>($"Error loading passages: {ex.Message}");
        }
    }

    private Result<float[][]> ComputeAllEmbeddings(string[] texts, string[] ids, string indexName, int batchSize)
    {
        var totalBatches = (texts.Length + batchSize - 1) / batchSize;
        var sw = Stopwatch.StartNew();

        // Sort passages by character length to minimize padding waste.
        // Similar-length passages batched together avoid padding short texts to the longest in the batch.
        var sortedIndices = CreateLengthSortedIndices(texts);
        var sortedTexts = sortedIndices.Select(i => texts[i]).ToArray();

        logger.LogInformation(
            "[{Name}] Computing embeddings: {Count} passages, batch size {Batch}, {Batches} batches (length-sorted)",
            indexName, texts.Length, batchSize, totalBatches);

        var sortedEmbeddings = new float[texts.Length][];

        for (int batch = 0; batch < totalBatches; batch++)
        {
            var start = batch * batchSize;
            var end = Math.Min(start + batchSize, sortedTexts.Length);
            var batchTexts = sortedTexts[start..end];

            var result = embeddingService.ComputeEmbeddings(batchTexts);
            if (result.IsFailure)
                return Result.Failure<float[][]>($"Batch {batch + 1}/{totalBatches} failed: {result.Error}");

            for (int i = 0; i < result.Value.Length; i++)
                sortedEmbeddings[start + i] = result.Value[i];

            if ((batch + 1) % 10 == 0 || batch == totalBatches - 1)
            {
                var elapsed = sw.Elapsed.TotalSeconds;
                var processed = Math.Min(end, texts.Length);
                var rate = processed / elapsed;
                logger.LogInformation("[{Name}] Batch {Current}/{Total} ({Pct}%) — {Rate:F0} passages/s",
                    indexName, batch + 1, totalBatches,
                    (int)((batch + 1) * 100.0 / totalBatches), rate);
            }
        }

        // Unsort embeddings back to original passage order
        var finalEmbeddings = UnsortEmbeddings(sortedEmbeddings, sortedIndices);

        sw.Stop();
        logger.LogInformation("[{Name}] Embeddings computed in {Elapsed:F1}s", indexName, sw.Elapsed.TotalSeconds);
        return Result.Success(finalEmbeddings);
    }

    /// <summary>
    /// Creates an index array sorted by text character length (ascending).
    /// Grouping similar-length passages reduces padding waste during tokenization.
    /// </summary>
    private static int[] CreateLengthSortedIndices(string[] texts)
    {
        return Enumerable.Range(0, texts.Length)
            .OrderBy(i => texts[i].Length)
            .ToArray();
    }

    /// <summary>
    /// Restores embeddings from sorted order back to original passage order.
    /// sortedIndices[sortedPos] = originalPos, so finalEmbeddings[originalPos] = sorted[sortedPos].
    /// </summary>
    private static float[][] UnsortEmbeddings(float[][] sortedEmbeddings, int[] sortedIndices)
    {
        var result = new float[sortedEmbeddings.Length][];
        for (int i = 0; i < sortedIndices.Length; i++)
            result[sortedIndices[i]] = sortedEmbeddings[i];
        return result;
    }

    private Result<float[][]> NormalizeEmbeddings(float[][] embeddings, string indexName)
    {
        logger.LogInformation("[{Name}] L2-normalizing {Count} embeddings", indexName, embeddings.Length);
        for (int i = 0; i < embeddings.Length; i++)
            embeddings[i] = VectorMath.L2Normalize(embeddings[i]);
        return Result.Success(embeddings);
    }

    private Result WriteOutputFiles(float[][] embeddings, string indexDir, string indexName)
    {
        try
        {
            var dimensions = embeddings.Length > 0 ? embeddings[0].Length : DefaultDimensions;

            WriteEmbeddingsBin(Path.Combine(indexDir, "documents.embeddings.bin"), embeddings, dimensions);
            WriteEmbeddingsMeta(Path.Combine(indexDir, "documents.embeddings.meta.json"), embeddings.Length, dimensions);

            logger.LogInformation("[{Name}] Wrote {Count} embeddings ({Dims}d) to disk",
                indexName, embeddings.Length, dimensions);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Error writing output files: {ex.Message}");
        }
    }

    private static void WriteEmbeddingsBin(string path, float[][] embeddings, int dimensions)
    {
        var rowBytes = dimensions * sizeof(float);
        using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1 << 20,
            useAsync: false);

        for (var i = 0; i < embeddings.Length; i++)
        {
            var row = embeddings[i];
            if (row.Length != dimensions)
                throw new InvalidOperationException(
                    $"Embedding row {i} has {row.Length} dims, expected {dimensions}.");
            stream.Write(MemoryMarshal.AsBytes(row.AsSpan()));
        }
    }

    private static void WriteEmbeddingsMeta(string path, int count, int dimensions)
    {
        var meta = new EmbeddingsMeta(count, dimensions, true);
        var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private static List<(string Name, string Dir)> DiscoverBuildableIndexes(string indexesDir)
    {
        return Directory.GetDirectories(indexesDir)
            .Where(d => File.Exists(Path.Combine(d, "documents.leann.meta.json")))
            .Select(d => (Name: Path.GetFileName(d)!, Dir: d))
            .OrderBy(x => x.Name)
            .ToList();
    }

    private sealed record EmbeddingsMeta(
        [property: JsonPropertyName("count")] int Count,
        [property: JsonPropertyName("dimensions")] int Dimensions,
        [property: JsonPropertyName("normalized")] bool Normalized);
}