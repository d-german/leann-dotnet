using LeannMcp.Models;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Detects PDF cover/title pages whose extracted text is dominated by a few
/// short boilerplate lines (e.g. product title + "Downloaded by &lt;name&gt; on &lt;date&gt;").
/// Such pages survive <see cref="HeaderFooterStripper"/> because they appear once
/// (not per page) and survive <see cref="TocPageDetector"/> because they are too
/// short to look like a TOC; they then get embedded as their own passage and add
/// pure noise to retrieval — see mrg id=0 ("Hyland Software Product Documentation
/// / Downloaded by Damon German on 2026-04-25").
///
/// Strictly page-1-only. A page qualifies when EITHER:
///   (a) it contains a "Downloaded by " stamp, OR
///   (b) it has fewer than <see cref="MaxLines"/> non-empty lines AND
///       average non-empty line length is under <see cref="MaxAvgLineLength"/> chars
///       AND no line ends with sentence-terminating punctuation.
/// </summary>
public static class TitlePageDetector
{
    private const int MaxLines = 10;
    private const double MaxAvgLineLength = 50.0;
    private const string DownloadedByMarker = "Downloaded by ";

    public static bool IsTitlePage(string pageText, int pageIndex)
    {
        if (pageIndex != 0)
            return false;

        if (pageText.Contains(DownloadedByMarker, StringComparison.OrdinalIgnoreCase))
            return true;

        var lines = SplitNonEmptyLines(pageText);
        if (lines.Count == 0 || lines.Count >= MaxLines)
            return false;

        var avgLength = lines.Average(l => (double)l.Length);
        if (avgLength >= MaxAvgLineLength)
            return false;

        return lines.All(HasNoTerminalPunctuation);
    }

    private static IReadOnlyList<string> SplitNonEmptyLines(string text) =>
        text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();

    private static bool HasNoTerminalPunctuation(string line) =>
        line[^1] is not ('.' or '!' or '?');

    public static IReadOnlyList<PageSegment> RemoveTitlePage(IReadOnlyList<PageSegment> pages) =>
        pages
            .Select((page, index) => (page, index))
            .Where(t => !IsTitlePage(t.page.Text, t.index))
            .Select(t => t.page)
            .ToArray();
}
