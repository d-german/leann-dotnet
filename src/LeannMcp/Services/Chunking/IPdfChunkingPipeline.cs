using CSharpFunctionalExtensions;
using LeannMcp.Models;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Dedicated chunking pipeline for PDF source documents. Kept separate from
/// <see cref="ICodeChunkStrategy"/> because PDFs need richer per-passage
/// metadata (page numbers, headings) and a different chunk-size regime than
/// either code or generic prose.
/// <para/>
/// Implementations are responsible for the full PDF-to-passages flow:
/// re-opening the PDF, page-segmenting, header/footer stripping, heading
/// detection, sentence-aware chunking within each page, and emitting
/// <see cref="PassageData"/> with metadata: <c>file_path</c>, <c>file_name</c>,
/// <c>source</c>, <c>source_type=pdf</c>, <c>page_start</c>, <c>page_end</c>,
/// optional <c>heading</c>.
/// </summary>
public interface IPdfChunkingPipeline
{
    /// <summary>
    /// Chunks the PDF identified by <paramref name="document"/> (using
    /// <see cref="SourceDocument.FilePath"/> as the source of truth — the
    /// pre-read flat <see cref="SourceDocument.Content"/> is ignored) and
    /// returns one <see cref="PassageData"/> per emitted chunk. Passage IDs
    /// start at <paramref name="firstPassageId"/> and increment by one per
    /// chunk in document order.
    /// </summary>
    Result<IReadOnlyList<PassageData>> Chunk(
        SourceDocument document,
        ChunkingOptions options,
        int firstPassageId);
}
