﻿using System.Globalization;
using System.Text;
using CSharpFunctionalExtensions;
using LeannMcp.Models;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Exceptions;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Reader for PDF files. Implements both <see cref="IDocumentReader"/>
/// (legacy single-string view, used by the generic discovery pipeline) and
/// <see cref="IStructuredDocumentReader"/> (per-page view, used by the PDF
/// chunking pipeline so page numbers can become first-class metadata and
/// header/footer/heading heuristics can run page-by-page).
/// <para/>
/// Encrypted, corrupt, or scanned/image-only PDFs (which yield no text)
/// return <see cref="Result.Failure"/> rather than throwing.
/// </summary>
public sealed class PdfDocumentReader(ILogger<PdfDocumentReader> logger)
    : IDocumentReader, IStructuredDocumentReader
{
    private const string PdfExtension = ".pdf";

    public bool CanHandle(string extension) =>
        string.Equals(extension, PdfExtension, StringComparison.OrdinalIgnoreCase);

    public Result<string> Read(string filePath) =>
        ReadPages(filePath)
            .Map(JoinPagesWithMarkers)
            .TapError(error => logger.LogWarning("PDF skipped: {Path}: {Error}", filePath, error));

    public Result<IReadOnlyList<PageSegment>> ReadStructured(string filePath) =>
        ReadPages(filePath)
            .TapError(error => logger.LogWarning("PDF skipped: {Path}: {Error}", filePath, error));

    private static Result<IReadOnlyList<PageSegment>> ReadPages(string filePath) =>
        TryOpen(filePath).Map(ExtractPages);

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

    private static IReadOnlyList<PageSegment> ExtractPages(PdfDocument doc)
    {
        using (doc)
        {
            var pages = new List<PageSegment>(doc.NumberOfPages);
            foreach (Page page in doc.GetPages())
            {
                pages.Add(new PageSegment(page.Number, page.Text ?? string.Empty));
            }
            return pages;
        }
    }

    private static string JoinPagesWithMarkers(IReadOnlyList<PageSegment> pages)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            if (i > 0)
                sb.Append("\n\n");
            sb.AppendFormat(CultureInfo.InvariantCulture, "--- Page {0} ---", page.Number);
            sb.Append("\n\n");
            sb.Append(page.Text);
        }
        return sb.ToString();
    }
}
