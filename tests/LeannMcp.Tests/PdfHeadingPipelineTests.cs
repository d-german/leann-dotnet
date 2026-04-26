using System.IO;
using LeannMcp.Services.Chunking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeannMcp.Tests;

/// <summary>
/// Integration tests that exercise the full PdfPig → PageLine → HeadingDetector
/// path against a small, deterministic in-memory PDF fixture. Pure helper
/// behavior is covered by HeadingDetectorTests; this file proves the adapter
/// (PdfPageLineExtractor + ReadLayout) wires through correctly.
/// </summary>
public class PdfHeadingPipelineTests
{
    private static PdfDocumentReader CreateReader() =>
        new(NullLogger<PdfDocumentReader>.Instance);

    [Fact]
    public void ReadLayout_ProducesLines_WithFontSizes_ForHeadingFixture()
    {
        var bytes = PdfFixtureBuilder.BuildPdfWithHeading(
            "Heading One",
            "Body line A.", "Body line B.", "Body line C.", "Body line D.");
        var path = PdfFixtureBuilder.WriteTempPdf(bytes);
        try
        {
            var result = CreateReader().ReadLayout(path);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "");
            var pages = result.Value;
            Assert.Single(pages);
            var (pageNumber, lines) = pages[0];
            Assert.Equal(1, pageNumber);
            Assert.NotEmpty(lines);
            Assert.All(lines, l => Assert.True(l.FontSize > 0));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HeadingDetector_OverPdfLayout_FindsTheLargeFontHeading()
    {
        var bytes = PdfFixtureBuilder.BuildPdfWithHeading(
            "Heading One",
            "Body line A.", "Body line B.", "Body line C.", "Body line D.");
        var path = PdfFixtureBuilder.WriteTempPdf(bytes);
        try
        {
            var layout = CreateReader().ReadLayout(path).Value;

            var headings = HeadingDetector.Detect(layout, minFontRatio: 1.3);

            Assert.Single(headings);
            Assert.Equal("Heading One", headings[0].Text);
            Assert.Equal(1, headings[0].PageNumber);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HeadingDetector_OverBodyOnlyPdf_FindsNoHeadings()
    {
        // No oversized line → false-positive guard.
        var bytes = PdfFixtureBuilder.BuildPdfWithHeading(
            "Body 0",
            "Body line A.", "Body line B.", "Body line C.", "Body line D.");
        var path = PdfFixtureBuilder.WriteTempPdf(bytes);
        try
        {
            // BuildPdfWithHeading still uses 18pt for the first param; rebuild with same size body
            // to simulate a body-only page.
            File.Delete(path);
            var bodyOnlyBytes = BuildBodyOnlyPdf("Body line A.", "Body line B.", "Body line C.", "Body line D.");
            var bodyOnlyPath = PdfFixtureBuilder.WriteTempPdf(bodyOnlyBytes);
            try
            {
                var layout = CreateReader().ReadLayout(bodyOnlyPath).Value;
                var headings = HeadingDetector.Detect(layout, minFontRatio: 1.3);
                Assert.Empty(headings);
            }
            finally
            {
                File.Delete(bodyOnlyPath);
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static byte[] BuildBodyOnlyPdf(params string[] lines)
    {
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        var y = 750;
        foreach (var line in lines)
        {
            page.AddText(line, 11, new UglyToad.PdfPig.Core.PdfPoint(50, y), font);
            y -= 18;
        }
        return builder.Build();
    }
}
