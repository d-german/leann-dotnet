using System.Text.Json;

namespace LeannMcp.Models;

public sealed record SearchResult(
    string Id,
    float Score,
    string Text,
    IReadOnlyDictionary<string, JsonElement>? Metadata = null);
