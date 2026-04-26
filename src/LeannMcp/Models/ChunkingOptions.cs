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

    /// <summary>
    /// Chunk size in characters for PDF prose. Defaults to 1600 — significantly
    /// larger than the generic <see cref="ChunkSize"/> because the previous
    /// default of 256 produced tiny duplicate-prone snippets on PDF input
    /// (root cause of the mid-2026 quality assessment).
    /// </summary>
    public int PdfChunkSize { get; init; } = 1600;

    /// <summary>
    /// Overlap in characters between consecutive PDF chunks. Defaults to 200
    /// (~12% of <see cref="PdfChunkSize"/>), much lower than the 50% overlap
    /// used by generic prose chunking — high overlap was the dominant cause of
    /// near-duplicates in PDF search results.
    /// </summary>
    public int PdfChunkOverlap { get; init; } = 200;

    /// <summary>
    /// Threshold for the header/footer boilerplate detector: a line is treated
    /// as boilerplate (and stripped before chunking) when it appears on at
    /// least this fraction of pages AND is shorter than the boilerplate
    /// length cap. 0.30 means "appears on 30% or more of pages". Range 0..1;
    /// 0 disables the heuristic.
    /// </summary>
    public double PdfBoilerplateRepeatRatio { get; init; } = 0.30;

    /// <summary>
    /// Multiplier of the document body font size above which a line is
    /// classified as a heading by the PDF heading detector. Default 1.3 means
    /// "lines whose median letter font size is at least 1.3× the document
    /// body median". Lower values produce more heading hits, higher values
    /// fewer.
    /// </summary>
    public double PdfMinHeadingFontRatio { get; init; } = 1.3;

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
