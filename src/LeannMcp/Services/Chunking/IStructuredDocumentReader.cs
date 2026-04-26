using CSharpFunctionalExtensions;
using LeannMcp.Models;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Optional companion contract to <see cref="IDocumentReader"/> for readers
/// that can produce a structured per-page representation of the source
/// document. PDF readers implement this so downstream pipelines (header/
/// footer stripping, heading detection, page-aware chunking) operate on a
/// list of <see cref="PageSegment"/> rather than re-parsing in-text page
/// markers.
/// <para/>
/// Plain-text readers deliberately do NOT implement this — there is no
/// page concept in a flat text file.
/// </summary>
public interface IStructuredDocumentReader
{
    /// <summary>
    /// Returns true if this reader can produce structured pages for the
    /// given file extension (including the leading dot, e.g. ".pdf").
    /// </summary>
    bool CanHandle(string extension);

    /// <summary>
    /// Reads the file at <paramref name="filePath"/> and returns its
    /// content as an ordered list of pages. Failures (missing file,
    /// encrypted PDF, corrupt PDF, etc.) are returned as
    /// <see cref="Result"/> failures rather than thrown.
    /// </summary>
    Result<IReadOnlyList<PageSegment>> ReadStructured(string filePath);
}
