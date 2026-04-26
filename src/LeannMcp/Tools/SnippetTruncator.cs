namespace LeannMcp.Tools;

/// <summary>
/// Pure helper that produces a bounded, human-friendly preview of a chunk's
/// text for display in MCP search results.
/// <para/>
/// Replaces the legacy <c>text[..200] + "..."</c> approach which chopped
/// mid-word ("mbnail URI…", "pPool…"). Instead, prefers — in order — a
/// sentence terminator, a newline, then a word boundary within the truncation
/// window. Only falls back to a hard mid-word cut when no boundary exists at
/// all (very rare for natural-language text).
/// </summary>
internal static class SnippetTruncator
{
    /// <summary>Default snippet length cap. Roughly two screenfuls in a typical MCP host.</summary>
    public const int DefaultMaxChars = 600;

    /// <summary>
    /// Fraction of <c>maxChars</c> that defines the lower bound of the boundary
    /// search window. A terminator at position 0.5*maxChars or later is
    /// preferred over truncating to maxChars; below that, we keep looking
    /// forward instead. 0.5 keeps snippets at least half the cap whenever
    /// possible without being so aggressive that natural sentence boundaries
    /// get rejected.
    /// </summary>
    private const double MinBoundaryRatio = 0.5;

    private static readonly string[] SentenceTerminators = { ". ", "! ", "? " };

    public static string Truncate(string? text, int maxChars = DefaultMaxChars)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.Length <= maxChars) return text;

        var minBoundary = (int)(maxChars * MinBoundaryRatio);
        var window = text[..maxChars];

        // 1. Sentence terminator anywhere in the window — prefer the latest one.
        var sentenceCut = FindLatestSentenceBoundary(window, minBoundary);
        if (sentenceCut > 0) return text[..sentenceCut].TrimEnd() + "…";

        // 2. Newline.
        var newlineCut = window.LastIndexOf('\n');
        if (newlineCut >= minBoundary) return text[..newlineCut].TrimEnd() + "…";

        // 3. Word boundary (last space) anywhere in the window.
        var spaceCut = window.LastIndexOf(' ');
        if (spaceCut >= minBoundary) return text[..spaceCut].TrimEnd() + "…";

        // 4. Last resort: hard cut at maxChars.
        return window + "…";
    }

    private static int FindLatestSentenceBoundary(string window, int minBoundary)
    {
        var best = -1;
        foreach (var term in SentenceTerminators)
        {
            var idx = window.LastIndexOf(term);
            if (idx >= minBoundary && idx + term.Length > best)
                best = idx + term.Length;
        }
        return best;
    }
}
