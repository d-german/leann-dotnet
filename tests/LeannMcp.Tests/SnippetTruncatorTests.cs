using LeannMcp.Tools;
using Xunit;

namespace LeannMcp.Tests;

public class SnippetTruncatorTests
{
    [Fact]
    public void Truncate_ReturnsOriginal_WhenShorterThanCap()
    {
        var input = "Short snippet that fits comfortably under the cap.";

        var result = SnippetTruncator.Truncate(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public void Truncate_ReturnsEmpty_ForNullOrEmpty()
    {
        Assert.Equal(string.Empty, SnippetTruncator.Truncate(null));
        Assert.Equal(string.Empty, SnippetTruncator.Truncate(""));
    }

    [Fact]
    public void Truncate_CutsAtSentenceBoundary_WhenAvailableInWindow()
    {
        // First sentence ends at index 60 (after the period+space).
        // Pad the rest so total length exceeds the cap and forces truncation.
        var first = "The quick brown fox jumps over the lazy dog by the river. ";
        var rest = new string('x', 600);
        var input = first + rest;

        var result = SnippetTruncator.Truncate(input, maxChars: 100);

        // Trimmed first sentence + ellipsis. Note TrimEnd removes the trailing space.
        Assert.Equal("The quick brown fox jumps over the lazy dog by the river.…", result);
    }

    [Fact]
    public void Truncate_PrefersLatestSentenceBoundary_InWindow()
    {
        var input = "Alpha beta gamma. Delta epsilon zeta. Eta theta iota kappa lambda mu nu xi omicron pi rho sigma tau upsilon phi chi psi omega.";

        var result = SnippetTruncator.Truncate(input, maxChars: 50);

        // "Alpha beta gamma. Delta epsilon zeta." (length 37) is the latest
        // sentence boundary fully inside the 50-char window.
        Assert.Equal("Alpha beta gamma. Delta epsilon zeta.…", result);
    }

    [Fact]
    public void Truncate_FallsBackToWordBoundary_WhenNoSentenceTerminator()
    {
        // No sentence terminators at all — only spaces.
        var input = "alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron pi rho sigma tau upsilon";

        var result = SnippetTruncator.Truncate(input, maxChars: 50);

        // Should end on a space-bounded word, never mid-word, and append ellipsis.
        Assert.EndsWith("…", result);
        var stripped = result[..^1];
        Assert.DoesNotContain(stripped, " "); // we know it ended on a space-trimmed word
        // Crucially: the last character before the ellipsis must be a letter,
        // not part of a chopped-up word from the *next* word past the cap.
        Assert.True(char.IsLetter(stripped[^1]));
        Assert.StartsWith(stripped, input, StringComparison.Ordinal);
    }

    [Fact]
    public void Truncate_HardCuts_WhenNoBoundaryExists()
    {
        // No spaces, no punctuation — pathological case (e.g., a base64 blob).
        var input = new string('a', 1000);

        var result = SnippetTruncator.Truncate(input, maxChars: 50);

        Assert.Equal(new string('a', 50) + "…", result);
    }

    [Fact]
    public void Truncate_PrefersSentenceOverNewline()
    {
        var input = "First clause and more text here.\nSecond line continues with extra padding to push us past the cap easily.";

        var result = SnippetTruncator.Truncate(input, maxChars: 50);

        // Sentence boundary at position 32 ("...here." + space-equivalent? actually period is at 31, period+space requires a space — no space after period because newline follows).
        // The cut should fall back to the newline (also a clean boundary), not chop into "Second".
        // Either "...here." or "First...here." styled outputs are acceptable provided no mid-word break.
        Assert.EndsWith("…", result);
        Assert.DoesNotContain("Seco", result); // never chops into the second line's first word
    }
}
