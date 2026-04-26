using System.IO;
using LeannMcp.Services.Chunking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeannMcp.Tests;

public class PdfDocumentReaderTests
{
    private static PdfDocumentReader CreateReader() =>
        new(NullLogger<PdfDocumentReader>.Instance);

    [Theory]
    [InlineData(".pdf", true)]
    [InlineData(".PDF", true)]
    [InlineData(".Pdf", true)]
    [InlineData(".txt", false)]
    [InlineData(".cs", false)]
    [InlineData("", false)]
    public void CanHandle_IsCaseInsensitive_AndOnlyMatchesPdf(string ext, bool expected)
    {
        Assert.Equal(expected, CreateReader().CanHandle(ext));
    }

    [Fact]
    public void Read_HappyPath_ReturnsTextWithPageMarkers()
    {
        var bytes = PdfFixtureBuilder.BuildTwoPagePdf("HELLO_PAGE_ONE", "HELLO_PAGE_TWO");
        var path = PdfFixtureBuilder.WriteTempPdf(bytes);
        try
        {
            var result = CreateReader().Read(path);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "");
            Assert.Contains("HELLO_PAGE_ONE", result.Value);
            Assert.Contains("HELLO_PAGE_TWO", result.Value);
            Assert.Contains("--- Page 1 ---", result.Value);
            Assert.Contains("--- Page 2 ---", result.Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_MissingFile_ReturnsFailure()
    {
        var path = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.pdf");
        var result = CreateReader().Read(path);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ReadStructured_HappyPath_ReturnsPerPageSegments()
    {
        var bytes = PdfFixtureBuilder.BuildTwoPagePdf("HELLO_PAGE_ONE", "HELLO_PAGE_TWO");
        var path = PdfFixtureBuilder.WriteTempPdf(bytes);
        try
        {
            var result = CreateReader().ReadStructured(path);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "");
            Assert.Equal(2, result.Value.Count);

            Assert.Equal(1, result.Value[0].Number);
            Assert.Contains("HELLO_PAGE_ONE", result.Value[0].Text);
            Assert.DoesNotContain("--- Page", result.Value[0].Text);

            Assert.Equal(2, result.Value[1].Number);
            Assert.Contains("HELLO_PAGE_TWO", result.Value[1].Text);
            Assert.DoesNotContain("--- Page", result.Value[1].Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadStructured_AndRead_ProduceConsistentTotalText()
    {
        var bytes = PdfFixtureBuilder.BuildTwoPagePdf("ALPHA_TOKEN", "BETA_TOKEN");
        var path = PdfFixtureBuilder.WriteTempPdf(bytes);
        try
        {
            var reader = CreateReader();
            var flat = reader.Read(path).Value;
            var structured = reader.ReadStructured(path).Value;

            // Every page's text must appear in the flat representation.
            foreach (var page in structured)
                Assert.Contains(page.Text, flat);

            // The flat representation adds page markers that ReadStructured
            // deliberately omits — they should not leak into any page text.
            Assert.Contains("--- Page 1 ---", flat);
            Assert.DoesNotContain("--- Page", structured[0].Text + structured[1].Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadStructured_MissingFile_ReturnsFailure()
    {
        var path = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.pdf");
        var result = CreateReader().ReadStructured(path);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ReadStructured_CorruptFile_ReturnsFailureWithoutThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"corrupt-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(path, "this is definitely not a PDF");
        try
        {
            var result = CreateReader().ReadStructured(path);
            Assert.True(result.IsFailure);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
