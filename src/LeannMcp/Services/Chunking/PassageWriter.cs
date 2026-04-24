using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using LeannMcp.Models;
using Microsoft.Extensions.Logging;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Writes the full set of index files that <see cref="IndexBuilder"/> can read.
/// Produces: passages.jsonl, passages.idx (JSON), ids.txt, meta.json, sync_roots.json.
/// </summary>
public sealed class PassageWriter(ILogger<PassageWriter> logger, EmbeddingModelDescriptor descriptor) : IPassageWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public Result WritePassages(
        string indexDir,
        string indexName,
        IReadOnlyList<PassageData> passages,
        IReadOnlyList<string> syncRoots)
    {
        try
        {
            Directory.CreateDirectory(indexDir);

            return WritePassagesJsonl(indexDir, passages)
                .Bind(() => WriteOffsetIndex(indexDir, passages))
                .Bind(() => WriteIdsFile(indexDir, passages))
                .Bind(() => WriteMetaJson(indexDir, descriptor))
                .Bind(() => WriteSyncRoots(indexDir, syncRoots))
                .Tap(() => logger.LogInformation(
                    "[{Name}] Wrote {Count} passages to {Dir}",
                    indexName, passages.Count, indexDir));
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to write passages: {ex.Message}");
        }
    }

    private static Result WritePassagesJsonl(string indexDir, IReadOnlyList<PassageData> passages)
    {
        try
        {
            var path = Path.Combine(indexDir, "documents.leann.passages.jsonl");
            using var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8);

            foreach (var passage in passages)
            {
                var json = JsonSerializer.Serialize(passage, JsonOptions);
                writer.WriteLine(json);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Error writing passages.jsonl: {ex.Message}");
        }
    }

    private static Result WriteOffsetIndex(string indexDir, IReadOnlyList<PassageData> passages)
    {
        try
        {
            // Write JSON offset map (replaces Python pickle .idx)
            var path = Path.Combine(indexDir, "documents.leann.passages.idx");
            var offsets = new Dictionary<string, long>();

            // Compute approximate offsets (matching JSONL line positions)
            long offset = 0;
            foreach (var passage in passages)
            {
                offsets[passage.Id] = offset;
                var line = JsonSerializer.Serialize(passage, JsonOptions);
                offset += System.Text.Encoding.UTF8.GetByteCount(line) + 1; // +1 for newline
            }

            var json = JsonSerializer.Serialize(offsets, PrettyJsonOptions);
            File.WriteAllText(path, json);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Error writing passages.idx: {ex.Message}");
        }
    }

    private static Result WriteIdsFile(string indexDir, IReadOnlyList<PassageData> passages)
    {
        try
        {
            var path = Path.Combine(indexDir, "documents.ids.txt");
            File.WriteAllLines(path, passages.Select(p => p.Id));
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Error writing ids.txt: {ex.Message}");
        }
    }

    private static Result WriteMetaJson(string indexDir, EmbeddingModelDescriptor descriptor)
    {
        try
        {
            var meta = new IndexMetadata(
                Version: "1.0",
                BackendName: "flat-cosine",
                EmbeddingModel: descriptor.Id,
                Dimensions: descriptor.Dimensions,
                EmbeddingMode: "onnx-directml",
                PassageSources:
                [
                    new PassageSource(
                        Type: "jsonl",
                        Path: "documents.leann.passages.jsonl",
                        IndexPath: "documents.leann.passages.idx",
                        PathRelative: "documents.leann.passages.jsonl",
                        IndexPathRelative: "documents.leann.passages.idx")
                ]);

            var path = Path.Combine(indexDir, "documents.leann.meta.json");
            var json = JsonSerializer.Serialize(meta, PrettyJsonOptions);
            File.WriteAllText(path, json);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Error writing meta.json: {ex.Message}");
        }
    }

    private static Result WriteSyncRoots(string indexDir, IReadOnlyList<string> syncRoots)
    {
        try
        {
            var config = new { roots = syncRoots, include_extensions = (string[]?)null, ignore_patterns = (string[]?)null };
            var path = Path.Combine(indexDir, "sync_roots.json");
            var json = JsonSerializer.Serialize(config, PrettyJsonOptions);
            File.WriteAllText(path, json);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Error writing sync_roots.json: {ex.Message}");
        }
    }
}
