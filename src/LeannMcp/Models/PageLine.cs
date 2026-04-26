namespace LeannMcp.Models;

/// <summary>
/// One visual line of text from a PDF page along with its predominant
/// font size. Produced by the PDF layout extractor and consumed by
/// <see cref="LeannMcp.Services.Chunking.HeadingDetector"/>.
/// </summary>
/// <param name="Text">The reconstructed text of the line (whitespace-trimmed).</param>
/// <param name="FontSize">Median font size (in PDF points) of glyphs on the line.</param>
public sealed record PageLine(string Text, double FontSize);

/// <summary>One heading detected by <see cref="LeannMcp.Services.Chunking.HeadingDetector"/>.</summary>
/// <param name="PageNumber">1-based PDF page number on which the heading appears.</param>
/// <param name="Text">Heading text (trimmed).</param>
/// <param name="FontSize">Median font size of the heading line (in PDF points).</param>
public sealed record Heading(int PageNumber, string Text, double FontSize);
