using LeannMcp.Models;
using Xunit;

namespace LeannMcp.Tests;

public class ChunkingOptionsPdfDefaultsTests
{
    [Fact]
    public void Defaults_ProvidePdfSpecificSizes()
    {
        var options = new ChunkingOptions();

        Assert.Equal(1600, options.PdfChunkSize);
        Assert.Equal(200, options.PdfChunkOverlap);
    }

    [Fact]
    public void Defaults_ProvidePdfHeuristicThresholds()
    {
        var options = new ChunkingOptions();

        Assert.Equal(0.30, options.PdfBoilerplateRepeatRatio, precision: 4);
        Assert.Equal(1.3, options.PdfMinHeadingFontRatio, precision: 4);
    }

    [Fact]
    public void PdfOverlap_IsMuchSmallerFraction_ThanGenericProseOverlap()
    {
        var options = new ChunkingOptions();

        var pdfRatio = (double)options.PdfChunkOverlap / options.PdfChunkSize;
        var proseRatio = (double)options.ChunkOverlap / options.ChunkSize;

        // PDF chunks must use significantly less overlap than the generic prose
        // chunker (50%). Excess overlap was the dominant cause of near-duplicates
        // in the mrg index quality assessment.
        Assert.True(pdfRatio < 0.20,
            $"PDF overlap ratio {pdfRatio:F3} should be well below the prose ratio {proseRatio:F3}");
    }

    [Fact]
    public void With_PdfFields_ProducesIndependentRecord()
    {
        var baseline = new ChunkingOptions();
        var customized = baseline with
        {
            PdfChunkSize = 2400,
            PdfChunkOverlap = 100,
            PdfBoilerplateRepeatRatio = 0.5,
            PdfMinHeadingFontRatio = 1.5,
        };

        // Customized values applied
        Assert.Equal(2400, customized.PdfChunkSize);
        Assert.Equal(100, customized.PdfChunkOverlap);
        Assert.Equal(0.5, customized.PdfBoilerplateRepeatRatio);
        Assert.Equal(1.5, customized.PdfMinHeadingFontRatio);

        // Original record unchanged (immutability)
        Assert.Equal(1600, baseline.PdfChunkSize);
        Assert.Equal(200, baseline.PdfChunkOverlap);
    }
}
