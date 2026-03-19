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
}
