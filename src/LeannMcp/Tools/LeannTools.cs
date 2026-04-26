using System.ComponentModel;
using System.Text;
using LeannMcp.Services;
using LeannMcp.Services.Workspace;
using ModelContextProtocol.Server;

namespace LeannMcp.Tools;

[McpServerToolType]
public sealed class LeannTools(IndexManager indexManager, WorkspaceResolver resolver)
{
    [McpServerTool(Name = "leann_search"), Description(
        """
        🔍 Search code using natural language - like having a coding assistant who knows your entire codebase!

        🎯 **Perfect for**:
        - "How does authentication work?" → finds auth-related code
        - "Error handling patterns" → locates try-catch blocks and error logic
        - "Database connection setup" → finds DB initialization code
        - "API endpoint definitions" → locates route handlers
        - "Configuration management" → finds config files and usage

        💡 **Pro tip**: Use this before making any changes to understand existing patterns and conventions.
        """)]
    public async Task<string> Search(
        McpServer server,
        [Description("Name of the LEANN index to search. Use 'leann_list' first to see available indexes.")]
        string index_name,
        [Description("Search query - can be natural language (e.g., 'how to handle errors') or technical terms (e.g., 'async function definition')")]
        string query,
        [Description("Number of search results to return. Use 5-10 for focused results, 15-20 for comprehensive exploration.")]
        int top_k = 5,
        [Description("Search complexity level. Use 16-32 for fast searches (recommended), 64+ for higher precision when needed.")]
        int complexity = 32,
        [Description("Include file paths and metadata in search results. Useful for understanding which files contain the results.")]
        bool show_metadata = false,
        [Description("Cosine-similarity threshold above which adjacent top-K results are treated as near-duplicates and filtered. Default 0.95 strips obvious clones (heavy chunk-overlap or boilerplate); set to 0 (or 1) to disable.")]
        double dedup_threshold = 0.95,
        CancellationToken cancellationToken = default)
    {
        await resolver.EnsureResolvedAsync(server, cancellationToken);
        var result = indexManager.Search(index_name, query, top_k, complexity, dedup_threshold);
        if (result.IsFailure)
            return $"Error: {result.Error}";

        return FormatSearchResults(query, result.Value, show_metadata);
    }

    [McpServerTool(Name = "leann_list"), Description(
        "📋 Show all your indexed codebases - your personal code library! Use this to see what's available for search.")]
    public async Task<string> List(McpServer server, CancellationToken cancellationToken = default)
    {
        await resolver.EnsureResolvedAsync(server, cancellationToken);
        var result = indexManager.ListIndexes();
        if (result.IsFailure)
            return $"Error: {result.Error}";

        return result.Value.Count == 0
            ? "No indexes found."
            : string.Join("\n", result.Value);
    }

    [McpServerTool(Name = "leann_warmup"), Description(
        "🚀 Pre-load the embedding model into memory. Call this FIRST before any leann_search. Takes 1-2 minutes on first call, but makes all subsequent leann_search calls complete in seconds. Returns available indexes and warmup timing.")]
    public async Task<string> Warmup(McpServer server, CancellationToken cancellationToken = default)
    {
        await resolver.EnsureResolvedAsync(server, cancellationToken);
        var result = indexManager.Warmup();
        return result.IsFailure ? $"Error: {result.Error}" : result.Value;
    }

    private static string FormatSearchResults(
        string query,
        IReadOnlyList<Models.SearchResult> results,
        bool showMetadata)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Search results for '{query}' (top {results.Count}):");

        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"{i + 1}. Score: {r.Score:F3}");

            if (showMetadata && r.Metadata is not null)
            {
                if (r.Metadata.TryGetValue("file_path", out var fp))
                    sb.AppendLine($"   File: {fp}");
                if (r.Metadata.TryGetValue("file_name", out var fn) && fn.ToString() != fp.ToString())
                    sb.AppendLine($"   Name: {fn}");
                if (r.Metadata.TryGetValue("creation_date", out var cd))
                    sb.AppendLine($"   Created: {cd}");
                if (r.Metadata.TryGetValue("last_modified_date", out var lm))
                    sb.AppendLine($"   Modified: {lm}");
            }

            var textPreview = SnippetTruncator.Truncate(r.Text);
            sb.AppendLine($"   {textPreview}");

            if (r.Metadata is not null && r.Metadata.TryGetValue("source", out var src))
                sb.AppendLine($"   Source: {src}");

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
