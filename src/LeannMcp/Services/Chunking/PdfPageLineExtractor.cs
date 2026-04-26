using LeannMcp.Models;
using UglyToad.PdfPig.Content;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Adapter that turns PdfPig's per-glyph <see cref="Letter"/> stream into a
/// list of <see cref="PageLine"/>s with median font sizes — the input shape
/// expected by <see cref="HeadingDetector"/>.
/// <para/>
/// Lines are reconstructed by grouping letters whose baseline Y-coordinate
/// rounds to the same integer (PDF points). Within each line the letters
/// are sorted left-to-right and concatenated; whitespace gaps wider than
/// half the font size become a single space. The median glyph font size on
/// the line is used as the line's font size.
/// </summary>
internal static class PdfPageLineExtractor
{
    public static IReadOnlyList<PageLine> Extract(Page page)
    {
        var letters = page.Letters;
        if (letters.Count == 0) return Array.Empty<PageLine>();

        var groups = letters
            .GroupBy(l => (int)Math.Round(l.StartBaseLine.Y))
            .OrderByDescending(g => g.Key); // PDF Y grows upward → page top first

        var lines = new List<PageLine>();
        foreach (var group in groups)
        {
            var ordered = group.OrderBy(l => l.StartBaseLine.X).ToList();
            var text = AssembleLine(ordered);
            var trimmed = text.Trim();
            if (trimmed.Length == 0) continue;

            var fontSize = MedianFontSize(ordered);
            lines.Add(new PageLine(trimmed, fontSize));
        }
        return lines;
    }

    private static string AssembleLine(List<Letter> ordered)
    {
        var sb = new System.Text.StringBuilder(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var letter = ordered[i];
            if (i > 0)
            {
                var prev = ordered[i - 1];
                var gap = letter.StartBaseLine.X - prev.EndBaseLine.X;
                if (gap > letter.FontSize * 0.25 && sb.Length > 0 && sb[^1] != ' ')
                    sb.Append(' ');
            }
            sb.Append(letter.Value);
        }
        return sb.ToString();
    }

    private static double MedianFontSize(List<Letter> letters)
    {
        var sizes = letters.Select(l => l.FontSize).Where(s => s > 0).ToList();
        if (sizes.Count == 0) return 0;
        sizes.Sort();
        var mid = sizes.Count / 2;
        return sizes.Count % 2 == 1 ? sizes[mid] : (sizes[mid - 1] + sizes[mid]) / 2.0;
    }
}
