using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeannMcp.Models;

public sealed record PassageData(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("metadata")] Dictionary<string, JsonElement>? Metadata = null);
