using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeannMcp.Models;
using LeannMcp.Tools;

namespace LeannMcp.Cli;

/// <summary>
/// Renders <see cref="SearchResult"/> lists for human and machine consumers.
/// Shared by MCP tool mode, the one-shot CLI (<c>--search</c>) and the resident
/// daemon (<c>--serve</c>) so every surface reports identical results.
/// </summary>
internal static class SearchResultFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ToText(
        string query,
        IReadOnlyList<SearchResult> results,
        bool showMetadata)
    {
        if (results.Count == 0)
            return $"No results for '{query}'.";

        var sb = new StringBuilder();
        sb.AppendLine($"Search results for '{query}' (top {results.Count}):");

        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"{i + 1}. Score: {r.Score:F3}");

            if (showMetadata && r.Metadata is not null)
            {
                var filePath = TryGetMetadata(r, "file_path");
                var fileName = TryGetMetadata(r, "file_name");

                if (filePath is not null) sb.AppendLine($"   File: {filePath}");
                if (fileName is not null && fileName != filePath) sb.AppendLine($"   Name: {fileName}");

                var created = TryGetMetadata(r, "creation_date");
                if (created is not null) sb.AppendLine($"   Created: {created}");

                var modified = TryGetMetadata(r, "last_modified_date");
                if (modified is not null) sb.AppendLine($"   Modified: {modified}");
            }

            sb.AppendLine($"   {SnippetTruncator.Truncate(r.Text)}");

            var source = TryGetMetadata(r, "source");
            if (source is not null) sb.AppendLine($"   Source: {source}");

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    public static string ToJson(
        string query,
        IReadOnlyList<SearchResult> results,
        bool showMetadata)
    {
        var payload = new
        {
            query,
            count = results.Count,
            results = results.Select((r, i) => new
            {
                rank = i + 1,
                score = r.Score,
                id = r.Id,
                file = TryGetMetadata(r, "file_path"),
                text = SnippetTruncator.Truncate(r.Text),
                metadata = showMetadata ? r.Metadata : null,
            }).ToList(),
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string? TryGetMetadata(SearchResult result, string key) =>
        result.Metadata is not null && result.Metadata.TryGetValue(key, out var value)
            ? value.ToString()
            : null;
}
