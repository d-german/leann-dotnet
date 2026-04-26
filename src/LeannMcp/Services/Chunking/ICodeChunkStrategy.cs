using LeannMcp.Models;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Strategy contract for splitting source code into chunks.
/// Implementations are language-aware (Roslyn for C#, brace-balanced for C-family,
/// fallback line-based for everything else). PDF and other non-code sources are
/// handled by their own pipelines (e.g. <c>IPdfChunkingPipeline</c>) so this
/// interface stays narrowly focused on programming languages.
/// </summary>
public interface ICodeChunkStrategy
{
    /// <summary>
    /// Returns true if this strategy can chunk content for the given language identifier
    /// (as defined in <see cref="FileExtensions.CodeLanguageMap"/>: "csharp", "typescript", etc.).
    /// </summary>
    bool CanHandle(string? language);

    /// <summary>
    /// Splits the given content into chunks. Returns plain strings; metadata is added
    /// downstream by <see cref="DocumentChunker"/>.
    /// </summary>
    IReadOnlyList<string> Chunk(string content, ChunkingOptions options);
}
