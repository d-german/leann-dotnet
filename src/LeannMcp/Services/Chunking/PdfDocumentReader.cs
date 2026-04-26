using System.Globalization;
using System.Text;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Exceptions;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// <see cref="IDocumentReader"/> for PDF files. Extracts text page-by-page
/// using <see cref="PdfDocument"/> from UglyToad.PdfPig and joins pages
/// with paragraph-style markers so the prose chunker can split on page
/// boundaries naturally. Encrypted, corrupt, or scanned/image-only PDFs
/// (which yield no text) return <see cref="Result.Failure"/> rather than
/// throwing.
/// </summary>
public sealed class PdfDocumentReader(ILogger<PdfDocumentReader> logger) : IDocumentReader
{
    private const string PdfExtension = ".pdf";

    public bool CanHandle(string extension) =>
        string.Equals(extension, PdfExtension, StringComparison.OrdinalIgnoreCase);

    public Result<string> Read(string filePath) =>
        TryOpen(filePath)
            .Map(ExtractPages)
            .Map(JoinPagesWithMarkers)
            .TapError(error => logger.LogWarning("PDF skipped: {Path}: {Error}", filePath, error));

    private static Result<PdfDocument> TryOpen(string filePath)
    {
        try
        {
            return Result.Success(PdfDocument.Open(filePath));
        }
        catch (PdfDocumentEncryptedException ex)
        {
            return Result.Failure<PdfDocument>($"PDF is encrypted: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Result.Failure<PdfDocument>($"Failed to open PDF: {ex.Message}");
        }
    }

    private static IReadOnlyList<(int Number, string Text)> ExtractPages(PdfDocument doc)
    {
        using (doc)
        {
            var pages = new List<(int, string)>(doc.NumberOfPages);
            foreach (Page page in doc.GetPages())
            {
                pages.Add((page.Number, page.Text ?? string.Empty));
            }
            return pages;
        }
    }

    private static string JoinPagesWithMarkers(IReadOnlyList<(int Number, string Text)> pages)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pages.Count; i++)
        {
            var (number, text) = pages[i];
            if (i > 0)
                sb.Append("\n\n");
            sb.AppendFormat(CultureInfo.InvariantCulture, "--- Page {0} ---", number);
            sb.Append("\n\n");
            sb.Append(text);
        }
        return sb.ToString();
    }
}
