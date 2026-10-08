using System.Text.Json;

namespace LeannMcp.Models;

/// <param name="IndexName">
/// The index the passage came from, set when one search spans several indexes. File
/// paths in <paramref name="Metadata"/> are relative to that index's source folder.
/// </param>
public sealed record SearchResult(
    string Id,
    float Score,
    string Text,
    IReadOnlyDictionary<string, JsonElement>? Metadata = null,
    string? IndexName = null);
