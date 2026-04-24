namespace LeannMcp.Models;

/// <summary>
/// Immutable configuration for the document chunking pipeline.
/// </summary>
public sealed record ChunkingOptions
{
    /// <summary>Chunk size in characters for text documents.</summary>
    public int ChunkSize { get; init; } = 256;

    /// <summary>Overlap in characters between consecutive text chunks.</summary>
    public int ChunkOverlap { get; init; } = 128;

    /// <summary>Chunk size in characters for code files.</summary>
    public int CodeChunkSize { get; init; } = 512;

    /// <summary>Overlap in characters between consecutive code chunks.</summary>
    public int CodeChunkOverlap { get; init; } = 64;

    /// <summary>Whether to include hidden files/directories (starting with '.').</summary>
    public bool IncludeHidden { get; init; }

    /// <summary>Custom file extensions to include. If null, uses <see cref="FileExtensions.AllSupported"/>.</summary>
    public IReadOnlySet<string>? IncludeExtensions { get; init; }

    /// <summary>
    /// Additional gitignore-style patterns to exclude (CLI <c>--exclude-paths</c>).
    /// Supports <c>**</c>, <c>*</c>, <c>?</c>. Patterns ending in <c>/</c> match directories only.
    /// Combined with patterns from any <c>.gitignore</c> files found in the tree.
    /// </summary>
    public IReadOnlyList<string>? ExcludePaths { get; init; }

    /// <summary>
    /// When true (default), code files are chunked using AST-aware strategies:
    /// Roslyn for C#, brace-balanced for TS/JS/Java/C-family. When false, falls
    /// back to the line-based sliding-window chunker for all code files. CLI
    /// flag <c>--no-ast</c> disables it; per-repo <c>"useAst": false</c> in
    /// repos.json disables it for a single repo.
    /// </summary>
    public bool UseAst { get; init; } = true;
}
