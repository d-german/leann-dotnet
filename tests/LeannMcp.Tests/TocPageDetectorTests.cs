using LeannMcp.Models;
using LeannMcp.Services.Chunking;
using Xunit;

namespace LeannMcp.Tests;

public sealed class TocPageDetectorTests
{
    // Real fixture from J:\.leann\indexes\mrg passage id=1 — extracted TOC text
    // with dotted leaders and page numbers already stripped by PdfPig.
    private const string TocPageText = """
        Documentation Notice
        Overview
        Introduction
        Version Compatibility
        Supported File Formats
        Licensing
        Installation
        Overview
        Deployment Architecture
        Components Required for All Deployments
        NilRead Components
        OnBase Components
        Configuring the API Server for Clinician Window
        XDS Components
        """;

    private const string ContentPageText = """
        BFF server caches the thumbnail URI requests for an interval of time
        to avoid round-trips to the API server. The caching interval is governed
        by the ThumbnailURICacheExpirationHours setting in the appsettings.json
        file. The default is 24 hours. Increasing this value reduces load on
        the API server but means thumbnail URI changes take longer to propagate.
        Restart IIS after editing the value for the change to take effect.
        """;

    private const string ShortNumberLineEdgeCase = """
        Step 1
        Step 2
        Step 3
        Continue when ready
        Step 4
        Step 5
        Done
        """;

    [Fact]
    public void IsTocPage_RealTocFixture_InFrontMatter_ReturnsTrue()
    {
        Assert.True(TocPageDetector.IsTocPage(TocPageText, pageIndex: 1, totalPages: 200));
    }

    [Fact]
    public void IsTocPage_ContentParagraph_ReturnsFalse()
    {
        Assert.False(TocPageDetector.IsTocPage(ContentPageText, pageIndex: 0, totalPages: 200));
    }

    [Fact]
    public void IsTocPage_ShortHeadingsButOutsideFrontMatter_ReturnsFalse()
    {
        // Same TOC-shaped text but on page 50 of 200 (25% in) — should NOT trigger.
        Assert.False(TocPageDetector.IsTocPage(TocPageText, pageIndex: 50, totalPages: 200));
    }

    [Fact]
    public void IsTocPage_ShortListWithoutEnoughLines_ReturnsFalse()
    {
        Assert.False(TocPageDetector.IsTocPage(ShortNumberLineEdgeCase, pageIndex: 0, totalPages: 200));
    }

    [Fact]
    public void RemoveTocPages_DropsTocAndKeepsContent()
    {
        // 20 pages total → front-matter cutoff = ceil(20 * 0.15) = 3, so pages 0..2 qualify.
        var pages = new List<PageSegment>
        {
            new(1, "Cover Page\nHyland Software Product Documentation"),
            new(2, TocPageText),
            new(3, ContentPageText),
        };
        for (var p = 4; p <= 20; p++)
            pages.Add(new PageSegment(p, ContentPageText));

        var result = TocPageDetector.RemoveTocPages(pages);

        Assert.Equal(19, result.Count);
        Assert.DoesNotContain(result, p => p.Number == 2);
        Assert.Contains(result, p => p.Number == 3);
    }
}
