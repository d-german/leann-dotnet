using LeannMcp.Models;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Detects table-of-contents (TOC) and front-matter index pages produced by PdfPig
/// text extraction, where dotted leaders and page numbers have already been stripped
/// and only a stack of short heading-shaped lines survives. Such pages otherwise leak
/// into the chunker as dense lists of section titles whose embeddings fuzzily match
/// almost any topical query, drowning out real content paragraphs in retrieval.
///
/// Conservative content-density heuristic — all three conditions must hold:
///   1. Page is in the first 15% of the document (by page index).
///   2. Page has at least 8 non-empty lines.
///   3. At least 80% of non-empty lines are "heading-like" (short and lack
///      sentence-terminating punctuation).
/// </summary>
public static class TocPageDetector
{
    private const int MinLines = 8;
    private const double MinHeadingRatio = 0.80;
    private const double FrontMatterFraction = 0.15;
    private const int MaxHeadingLineLength = 80;

    public static bool IsTocPage(string pageText, int pageIndex, int totalPages)
    {
        if (!IsInFrontMatter(pageIndex, totalPages))
            return false;

        var lines = SplitNonEmptyLines(pageText);
        if (lines.Count < MinLines)
            return false;

        var headingCount = lines.Count(IsHeadingLike);
        return (double)headingCount / lines.Count >= MinHeadingRatio;
    }

    private static bool IsInFrontMatter(int pageIndex, int totalPages)
    {
        if (totalPages <= 0) return false;
        var cutoff = Math.Max(1, (int)Math.Ceiling(totalPages * FrontMatterFraction));
        return pageIndex < cutoff;
    }

    private static IReadOnlyList<string> SplitNonEmptyLines(string text) =>
        text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();

    private static bool IsHeadingLike(string line)
    {
        if (line.Length > MaxHeadingLineLength) return false;
        var last = line[^1];
        return last is not ('.' or '!' or '?');
    }

    public static IReadOnlyList<PageSegment> RemoveTocPages(IReadOnlyList<PageSegment> pages)
    {
        var totalPages = pages.Count;
        return pages
            .Select((page, index) => (page, index))
            .Where(t => !IsTocPage(t.page.Text, t.index, totalPages))
            .Select(t => t.page)
            .ToArray();
    }
}
