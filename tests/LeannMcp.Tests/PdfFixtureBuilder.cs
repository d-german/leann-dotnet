using System.IO;
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
