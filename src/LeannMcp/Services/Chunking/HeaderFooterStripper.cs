using LeannMcp.Models;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Removes repeating header / footer / running-title boilerplate lines from a
/// list of <see cref="PageSegment"/>s before they are chunked.
/// <para/>
/// Algorithm: count the page-frequency of every short trimmed line across the
/// document; lines whose frequency meets <c>repeatRatio</c> (default 0.30 of
/// pages) AND whose length is below <see cref="MaxBoilerplateLineLength"/> are
/// considered boilerplate and stripped from every page they appear on. The
/// length cap prevents accidentally removing real body paragraphs that happen
/// to recur (citations, definitions, etc.).
/// <para/>
/// Pure function — input is unchanged, a fresh list is returned. Idempotent:
/// <c>Strip(Strip(x)) == Strip(x)</c>.
/// <para/>
/// User-reported defect D3 (footer leakage like
/// "Hyland Clinician Window© Hyland Software, Inc. and its affiliates.202"
/// embedded in body chunks) is what this addresses. Stripping these before
/// embedding cleans the semantic vector and reduces near-duplicate noise.
/// </summary>
public static class HeaderFooterStripper
{
    /// <summary>
    /// Lines longer than this many characters are never treated as boilerplate
    /// even if they repeat — body paragraphs sometimes recur (e.g. legal disclaimer
    /// re-printed verbatim in an appendix), and we'd rather keep them than
    /// over-strip.
    /// </summary>
    public const int MaxBoilerplateLineLength = 120;

    /// <summary>
    /// Returns a new list of pages with repeating short lines removed.
    /// <paramref name="repeatRatio"/> is the fraction of pages (0..1) on which
    /// a line must appear to be considered boilerplate. Values outside
    /// (0, 1] disable stripping (input returned unchanged but as a fresh list).
    /// </summary>
    public static IReadOnlyList<PageSegment> Strip(
        IReadOnlyList<PageSegment> pages,
        double repeatRatio = 0.30)
    {
        if (pages.Count == 0) return Array.Empty<PageSegment>();
        if (repeatRatio <= 0.0 || repeatRatio > 1.0) return pages.ToList();

        var boilerplate = IdentifyBoilerplate(pages, repeatRatio);
        if (boilerplate.Count == 0) return pages.ToList();

        return pages.Select(p => StripLines(p, boilerplate)).ToList();
    }

    private static HashSet<string> IdentifyBoilerplate(
        IReadOnlyList<PageSegment> pages, double repeatRatio)
    {
        var threshold = (int)Math.Ceiling(pages.Count * repeatRatio);
        if (threshold < 2) threshold = 2; // single-occurrence isn't boilerplate

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var page in pages)
        {
            // Use a per-page set so a line repeated within one page still only
            // counts once toward the cross-page frequency.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in EnumerateCandidateLines(page.Text))
                seen.Add(line);

            foreach (var line in seen)
                counts[line] = counts.GetValueOrDefault(line) + 1;
        }

        var boilerplate = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (line, count) in counts)
            if (count >= threshold) boilerplate.Add(line);
        return boilerplate;
    }

    private static IEnumerable<string> EnumerateCandidateLines(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.Length >= MaxBoilerplateLineLength)
                continue;
            yield return trimmed;
        }
    }

    private static PageSegment StripLines(PageSegment page, HashSet<string> boilerplate)
    {
        var lines = page.Text.Split('\n');
        var kept = new List<string>(lines.Length);
        foreach (var raw in lines)
        {
            if (boilerplate.Contains(raw.Trim())) continue;
            kept.Add(raw);
        }
        var stripped = string.Join('\n', kept).Trim();
        return new PageSegment(page.Number, stripped);
    }
}
