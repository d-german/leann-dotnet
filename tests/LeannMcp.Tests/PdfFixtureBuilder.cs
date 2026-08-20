using System.IO;
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace LeannMcp.Tests;

/// <summary>
/// Builds tiny multi-page PDFs in-memory for tests. Avoids checking
/// binary fixtures into the repo.
/// </summary>
internal static class PdfFixtureBuilder
{
    public static byte[] BuildTwoPagePdf(string page1Text, string page2Text)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var page1 = builder.AddPage(PageSize.A4);
        page1.AddText(page1Text, 12, new PdfPoint(50, 750), font);

        var page2 = builder.AddPage(PageSize.A4);
        page2.AddText(page2Text, 12, new PdfPoint(50, 750), font);

        return builder.Build();
    }

    public static byte[] BuildPdfWithHeading(string heading, params string[] bodyLines)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var page = builder.AddPage(PageSize.A4);
        page.AddText(heading, 18, new PdfPoint(50, 780), font);
        var y = 750;
        foreach (var line in bodyLines)
        {
            page.AddText(line, 11, new PdfPoint(50, y), font);
            y -= 18;
        }
        return builder.Build();
    }

    /// <summary>
    /// Builds a Type 0 font with indirect CIDSystemInfo strings and a
    /// deliberately invalid embedded font. PdfPig opens this document but
    /// throws only when the page and its font are materialized.
    /// </summary>
    public static byte[] BuildPdfWithLazyFontFailure()
    {
        const string content = "BT /F1 12 Tf 50 750 Td <0001> Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type0 /BaseFont /TestFont /Encoding /Identity-H /DescendantFonts [ << /Type /Font /Subtype /CIDFontType2 /BaseFont /TestFont /CIDSystemInfo << /Registry 6 0 R /Ordering 7 0 R /Supplement 0 >> /FontDescriptor << /Type /FontDescriptor /FontName /TestFont /Flags 4 /FontBBox [0 -200 1000 900] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile2 8 0 R >> /CIDToGIDMap /Identity /DW 1000 >> ] >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream",
            "(Adobe)",
            "(Identity)",
            "<< /Length 1 >>\nstream\n0\nendstream",
        };

        using var output = new MemoryStream();
        WriteAscii(output, "%PDF-1.4\n");
        var offsets = new List<long>(objects.Length);
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(output.Position);
            WriteAscii(output, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xrefOffset = output.Position;
        WriteAscii(output, $"xref\n0 {objects.Length + 1}\n");
        WriteAscii(output, "0000000000 65535 f \n");
        foreach (var offset in offsets)
            WriteAscii(output, $"{offset:0000000000} 00000 n \n");
        WriteAscii(output, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return output.ToArray();
    }

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes);
    }

    /// <summary>
    /// Builds a multi-page PDF where every page carries the same footer
    /// (boilerplate exercise for HeaderFooterStripper) and the first page
    /// has an oversized heading (exercise for HeadingDetector). Each page's
    /// body is a list of unique lines so chunkers can be tested for
    /// page-boundary respect.
    /// </summary>
    public static byte[] BuildPdfWithBoilerplateAndHeading(
        string heading,
        string sharedFooter,
        IReadOnlyList<IReadOnlyList<string>> perPageBody)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var pageIndex = 0; pageIndex < perPageBody.Count; pageIndex++)
        {
            var page = builder.AddPage(PageSize.A4);
            var y = 780;
            if (pageIndex == 0)
            {
                page.AddText(heading, 18, new PdfPoint(50, y), font);
                y -= 30;
            }
            foreach (var line in perPageBody[pageIndex])
            {
                page.AddText(line, 11, new PdfPoint(50, y), font);
                y -= 18;
            }
            page.AddText(sharedFooter, 9, new PdfPoint(50, 40), font);
        }
        return builder.Build();
    }

    public static string WriteTempPdf(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"leann-pdf-test-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
