using LeannMcp.Models;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Pure detector that identifies which lines of a PDF look like section
/// headings based on font-size analysis. Operates on extracted
/// <see cref="PageLine"/> data so it can be unit-tested without real PDFs.
/// <para/>
/// Algorithm: compute the document-wide median body font size from all
/// non-empty lines; flag every line whose font size is at least
/// <paramref name="minFontRatio"/> times the body median (default 1.3 from
/// <see cref="LeannMcp.Models.ChunkingOptions.PdfMinHeadingFontRatio"/>).
/// <para/>
/// Resolves user-reported defect D4 from the mrg quality assessment ("no
/// section/heading metadata"). Promoting headings to first-class metadata
/// lets RAG/LLM citations say "see section X on page Y" instead of relying
/// on raw text.
/// </summary>
public static class HeadingDetector
{
    /// <summary>
    /// Returns headings detected across <paramref name="pages"/>. Each entry
    /// of <paramref name="pages"/> is a 1-based page number paired with that
    /// page's lines (with font sizes). Pages without any non-empty lines
    /// contribute nothing. If the document has no body text or the ratio is
    /// outside (1, infinity), an empty list is returned.
    /// </summary>
    public static IReadOnlyList<Heading> Detect(
        IReadOnlyList<(int PageNumber, IReadOnlyList<PageLine> Lines)> pages,
        double minFontRatio = 1.3)
    {
        if (pages.Count == 0 || minFontRatio <= 1.0) return Array.Empty<Heading>();

        var bodyMedian = ComputeBodyMedian(pages);
        if (bodyMedian <= 0) return Array.Empty<Heading>();

        var threshold = bodyMedian * minFontRatio;
        var headings = new List<Heading>();
        foreach (var (pageNumber, lines) in pages)
        {
            foreach (var line in lines)
            {
                var text = line.Text.Trim();
                if (text.Length == 0) continue;
                if (line.FontSize >= threshold)
                    headings.Add(new Heading(pageNumber, text, line.FontSize));
            }
        }
        return headings;
    }

    private static double ComputeBodyMedian(
        IReadOnlyList<(int PageNumber, IReadOnlyList<PageLine> Lines)> pages)
    {
        var sizes = new List<double>();
        foreach (var (_, lines) in pages)
            foreach (var line in lines)
                if (!string.IsNullOrWhiteSpace(line.Text) && line.FontSize > 0)
                    sizes.Add(line.FontSize);

        return Median(sizes);
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1
            ? values[mid]
            : (values[mid - 1] + values[mid]) / 2.0;
    }
}
