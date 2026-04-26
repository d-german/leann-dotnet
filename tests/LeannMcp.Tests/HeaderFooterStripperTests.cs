using LeannMcp.Models;
using LeannMcp.Services.Chunking;
using Xunit;

namespace LeannMcp.Tests;

/// <summary>
/// Unit tests for <see cref="HeaderFooterStripper"/>. Each test constructs a
/// small synthetic document with controlled repeat ratios so the heuristic's
/// behavior is exact and the threshold rounding is exercised explicitly.
/// </summary>
public class HeaderFooterStripperTests
{
    [Fact]
    public void Strip_RemovesLine_WhenRepeatedOnAtLeastRatioOfPages()
    {
        // Footer appears on 4/5 pages (80% >= 30% threshold).
        var pages = new[]
        {
            Page(1, "Body of page 1.\nHyland Clinician Window © 2024"),
            Page(2, "Body of page 2.\nHyland Clinician Window © 2024"),
            Page(3, "Different body 3.\nHyland Clinician Window © 2024"),
            Page(4, "Page 4 content.\nHyland Clinician Window © 2024"),
            Page(5, "Page 5 unique."),
        };

        var stripped = HeaderFooterStripper.Strip(pages, repeatRatio: 0.30);

        foreach (var p in stripped)
            Assert.DoesNotContain("Hyland", p.Text);
        Assert.Contains("Body of page 1.", stripped[0].Text);
    }

    [Fact]
    public void Strip_KeepsLine_WhenRepeatBelowThreshold()
    {
        // "Note." appears on 2/10 pages = 20% < 30% threshold.
        var pages = Enumerable.Range(1, 10)
            .Select(i => Page(i, i <= 2 ? $"Body {i}.\nNote." : $"Body {i}."))
            .ToList();

        var stripped = HeaderFooterStripper.Strip(pages, repeatRatio: 0.30);

        Assert.Contains("Note.", stripped[0].Text);
        Assert.Contains("Note.", stripped[1].Text);
    }

    [Fact]
    public void Strip_KeepsLongLine_EvenWhenRepeating()
    {
        // A 200-char "footer" should NOT be stripped despite repeating —
        // the length cap protects body paragraphs that recur verbatim.
        var longLine = new string('x', 200);
        var pages = Enumerable.Range(1, 5)
            .Select(i => Page(i, $"Body {i}.\n{longLine}"))
            .ToList();

        var stripped = HeaderFooterStripper.Strip(pages, repeatRatio: 0.30);

        foreach (var p in stripped)
            Assert.Contains(longLine, p.Text);
    }

    [Fact]
    public void Strip_ReturnsInputUnchanged_WhenNoLineRepeats()
    {
        var pages = new[]
        {
            Page(1, "Alpha line."),
            Page(2, "Beta line."),
            Page(3, "Gamma line."),
        };

        var stripped = HeaderFooterStripper.Strip(pages, repeatRatio: 0.30);

        Assert.Equal(pages.Length, stripped.Count);
        for (var i = 0; i < pages.Length; i++)
            Assert.Equal(pages[i].Text, stripped[i].Text);
    }

    [Fact]
    public void Strip_IsIdempotent()
    {
        var pages = new[]
        {
            Page(1, "Body 1.\nFooter X"),
            Page(2, "Body 2.\nFooter X"),
            Page(3, "Body 3.\nFooter X"),
        };

        var once = HeaderFooterStripper.Strip(pages);
        var twice = HeaderFooterStripper.Strip(once);

        Assert.Equal(once.Count, twice.Count);
        for (var i = 0; i < once.Count; i++)
            Assert.Equal(once[i].Text, twice[i].Text);
    }

    [Fact]
    public void Strip_DoesNotMutateInput()
    {
        var pages = new[]
        {
            Page(1, "Body 1.\nFooter Y"),
            Page(2, "Body 2.\nFooter Y"),
            Page(3, "Body 3.\nFooter Y"),
        };
        var snapshot = pages.Select(p => p.Text).ToArray();

        _ = HeaderFooterStripper.Strip(pages);

        for (var i = 0; i < pages.Length; i++)
            Assert.Equal(snapshot[i], pages[i].Text);
    }

    [Fact]
    public void Strip_ReturnsEmpty_WhenNoPages()
    {
        var stripped = HeaderFooterStripper.Strip(Array.Empty<PageSegment>());
        Assert.Empty(stripped);
    }

    [Fact]
    public void Strip_DisabledRatio_ReturnsInputUnchanged()
    {
        var pages = new[]
        {
            Page(1, "Body 1.\nFooter Z"),
            Page(2, "Body 2.\nFooter Z"),
        };

        var disabled = HeaderFooterStripper.Strip(pages, repeatRatio: 0.0);

        Assert.Equal(pages.Length, disabled.Count);
        for (var i = 0; i < pages.Length; i++)
            Assert.Equal(pages[i].Text, disabled[i].Text);
    }

    private static PageSegment Page(int n, string text) => new(n, text);
}
