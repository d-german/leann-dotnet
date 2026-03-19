using System.Text.Json.Serialization;

namespace LeannMcp.Models;

public sealed record PassageSource(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("index_path")] string? IndexPath,
    [property: JsonPropertyName("path_relative")] string? PathRelative,
    [property: JsonPropertyName("index_path_relative")] string? IndexPathRelative);

public sealed record IndexMetadata(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("backend_name")] string BackendName,
    [property: JsonPropertyName("embedding_model")] string EmbeddingModel,
    [property: JsonPropertyName("dimensions")] int Dimensions,
    [property: JsonPropertyName("embedding_mode")] string? EmbeddingMode,
    [property: JsonPropertyName("passage_sources")] IReadOnlyList<PassageSource> PassageSources,
    [property: JsonPropertyName("is_compact")] bool? IsCompact = null,
    [property: JsonPropertyName("is_pruned")] bool? IsPruned = null);
