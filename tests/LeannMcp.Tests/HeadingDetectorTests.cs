using LeannMcp.Models;
using LeannMcp.Services.Chunking;
using Xunit;

namespace LeannMcp.Tests;

/// <summary>
/// Unit tests for <see cref="HeadingDetector"/>. Synthetic PageLine data is
/// used so the threshold heuristic is exercised independently of any real
/// PDF — that integration is exercised by T09's pipeline tests.
/// </summary>
public class HeadingDetectorTests
{
    [Fact]
    public void Detect_FindsSingleHeading_WhenOneLineExceedsRatio()
    {
        var pages = new List<(int, IReadOnlyList<PageLine>)>
        {
            (1, new[]
            {
                new PageLine("Section 1: Overview", 16.0),
                new PageLine("Body line one.", 11.0),
                new PageLine("Body line two.", 11.0),
                new PageLine("Body line three.", 11.0),
            }),
        };

        var headings = HeadingDetector.Detect(pages, minFontRatio: 1.3);

        Assert.Single(headings);
        Assert.Equal("Section 1: Overview", headings[0].Text);
        Assert.Equal(1, headings[0].PageNumber);
        Assert.Equal(16.0, headings[0].FontSize);
    }

    [Fact]
    public void Detect_ReturnsNoHeadings_WhenAllLinesNearBodyFont()
    {
        var pages = new List<(int, IReadOnlyList<PageLine>)>
        {
            (1, new[]
            {
                new PageLine("Body 1.", 11.0),
                new PageLine("Body 2.", 11.5),
                new PageLine("Body 3.", 12.0),
            }),
        };

        var headings = HeadingDetector.Detect(pages, minFontRatio: 1.3);

        Assert.Empty(headings);
    }

    [Fact]
    public void Detect_RespectsConfigurableRatio()
    {
        var pages = new List<(int, IReadOnlyList<PageLine>)>
        {
            (1, new[]
            {
                new PageLine("Mildly larger header", 13.0),
                new PageLine("Body line.", 11.0),
                new PageLine("Body line.", 11.0),
                new PageLine("Body line.", 11.0),
            }),
        };

        var strict = HeadingDetector.Detect(pages, minFontRatio: 1.3);
        var relaxed = HeadingDetector.Detect(pages, minFontRatio: 1.15);

        Assert.Empty(strict);
        Assert.Single(relaxed);
        Assert.Equal("Mildly larger header", relaxed[0].Text);
    }

    [Fact]
    public void Detect_AssignsCorrectPageNumber_AcrossMultiplePages()
    {
        var pages = new List<(int, IReadOnlyList<PageLine>)>
        {
            (1, new[]
            {
                new PageLine("Body of page one.", 11.0),
                new PageLine("More body.", 11.0),
            }),
            (2, new[]
            {
                new PageLine("Section: Page Two Title", 16.0),
                new PageLine("Body of page two.", 11.0),
                new PageLine("More body.", 11.0),
            }),
        };

        var headings = HeadingDetector.Detect(pages);

        Assert.Single(headings);
        Assert.Equal(2, headings[0].PageNumber);
    }

    [Fact]
    public void Detect_IgnoresEmptyLines()
    {
        var pages = new List<(int, IReadOnlyList<PageLine>)>
        {
            (1, new[]
            {
                new PageLine("   ", 99.0),
                new PageLine("Real heading", 16.0),
                new PageLine("Body.", 11.0),
                new PageLine("Body.", 11.0),
                new PageLine("Body.", 11.0),
            }),
        };

        var headings = HeadingDetector.Detect(pages);

        Assert.Single(headings);
        Assert.Equal("Real heading", headings[0].Text);
    }

    [Fact]
    public void Detect_ReturnsEmpty_ForEmptyInput()
    {
        var headings = HeadingDetector.Detect(
            Array.Empty<(int, IReadOnlyList<PageLine>)>());
        Assert.Empty(headings);
    }

    [Fact]
    public void Detect_ReturnsEmpty_WhenRatioBelowOne()
    {
        var pages = new List<(int, IReadOnlyList<PageLine>)>
        {
            (1, new[] { new PageLine("Big text", 24.0), new PageLine("Body.", 11.0) }),
        };

        var headings = HeadingDetector.Detect(pages, minFontRatio: 0.5);

        Assert.Empty(headings);
    }
}
