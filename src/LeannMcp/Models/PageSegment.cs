namespace LeannMcp.Models;

/// <summary>
/// One page of a structured document (currently only PDF). Immutable by
/// construction. <see cref="Number"/> is 1-based to match the convention
/// used by PdfPig and by humans referring to PDF pages.
/// <para/>
/// Used by <see cref="LeannMcp.Services.Chunking.IStructuredDocumentReader"/>
/// to deliver per-page text without the legacy <c>--- Page N ---</c> in-text
/// markers — the page number becomes first-class metadata that the PDF
/// chunking pipeline can promote onto every emitted passage.
/// </summary>
/// <param name="Number">1-based page number as reported by the source document.</param>
/// <param name="Text">Raw text content of the page (may be empty for image-only pages).</param>
public sealed record PageSegment(int Number, string Text);
