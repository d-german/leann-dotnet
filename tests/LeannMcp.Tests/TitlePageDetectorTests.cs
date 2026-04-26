using LeannMcp.Models;
using LeannMcp.Services.Chunking;
using Xunit;

namespace LeannMcp.Tests;

public sealed class TitlePageDetectorTests
{
    // Real fixture from J:\.leann\indexes\mrg passage id=0
    private const string MrgTitlePageText = """
        Hyland Software Product Documentation
        Downloaded by Damon German on 2026-04-25
        """;

    private const string RealIntroParagraph = """
        This document describes the configuration and deployment of the Clinician
        Window product. It assumes the reader is familiar with the OnBase platform
        and the Hyland NilRead viewer. The remainder of the introduction enumerates
        the supported environments, version pairings, and licensing prerequisites
        that gate a successful production rollout.
        """;

    [Fact]
    public void IsTitlePage_DownloadedByOnPage1_ReturnsTrue()
    {
        Assert.True(TitlePageDetector.IsTitlePage(MrgTitlePageText, pageIndex: 0));
    }

    [Fact]
    public void IsTitlePage_SameTextOnPage2_ReturnsFalse()
    {
        // Page-1-only filter must not strip mid-document boilerplate (different defect).
        Assert.False(TitlePageDetector.IsTitlePage(MrgTitlePageText, pageIndex: 1));
    }

    [Fact]
    public void IsTitlePage_RealIntroductionOnPage1_ReturnsFalse()
    {
        Assert.False(TitlePageDetector.IsTitlePage(RealIntroParagraph, pageIndex: 0));
    }

    [Fact]
    public void RemoveTitlePage_DropsOnlyPage1Match()
    {
        var pages = new List<PageSegment>
        {
            new(1, MrgTitlePageText),
            new(2, RealIntroParagraph),
            new(3, RealIntroParagraph),
        };

        var result = TitlePageDetector.RemoveTitlePage(pages);

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, p => p.Number == 1);
    }

    [Fact]
    public void RemoveTitlePage_KeepsRealFirstPage()
    {
        var pages = new List<PageSegment>
        {
            new(1, RealIntroParagraph),
            new(2, RealIntroParagraph),
        };

        var result = TitlePageDetector.RemoveTitlePage(pages);

        Assert.Equal(2, result.Count);
    }
}
