using LeannMcp.Services.Chunking;
using Xunit;

namespace LeannMcp.Tests;

/// <summary>
/// Bracket expressions in ignore patterns. The stock Visual Studio .gitignore
/// excludes build output as <c>[Bb]in/</c> and <c>[Oo]bj/</c>, so a matcher that
/// treats '[' as a literal silently indexes every repository's build output.
/// </summary>
public sealed class GitIgnoreCharClassTests
{
    [Theory]
    [InlineData("obj", "[Oo]bj")]
    [InlineData("Obj", "[Oo]bj")]
    [InlineData("bin", "[Bb]in")]
    [InlineData("Bin", "[Bb]in")]
    public void MatchesGlob_VisualStudioBuildOutputPatterns_Match(string path, string pattern)
    {
        Assert.True(GitIgnoreFilter.MatchesGlob(path, pattern));
    }

    [Theory]
    [InlineData("xbj", "[Oo]bj")]
    [InlineData("obk", "[Oo]bj")]
    public void MatchesGlob_NonMembers_DoNotMatch(string path, string pattern)
    {
        Assert.False(GitIgnoreFilter.MatchesGlob(path, pattern));
    }

    [Theory]
    [InlineData("a", "[a-z]")]
    [InlineData("m", "[a-z]")]
    [InlineData("z", "[a-z]")]
    [InlineData("5", "[0-9]")]
    public void MatchesGlob_Ranges_Match(string path, string pattern)
    {
        Assert.True(GitIgnoreFilter.MatchesGlob(path, pattern));
    }

    [Theory]
    [InlineData("A", "[a-z]")]
    [InlineData("a", "[0-9]")]
    public void MatchesGlob_OutsideRange_DoesNotMatch(string path, string pattern)
    {
        Assert.False(GitIgnoreFilter.MatchesGlob(path, pattern));
    }

    [Theory]
    [InlineData("a", "[!0-9]", true)]
    [InlineData("5", "[!0-9]", false)]
    [InlineData("a", "[^0-9]", true)]
    [InlineData("5", "[^0-9]", false)]
    public void MatchesGlob_NegatedClass_InvertsMembership(string path, string pattern, bool expected)
    {
        Assert.Equal(expected, GitIgnoreFilter.MatchesGlob(path, pattern));
    }

    [Fact]
    public void MatchesGlob_ClassCombinedWithWildcards_Matches()
    {
        Assert.True(GitIgnoreFilter.MatchesGlob("src/obj/project.json", "src/[Oo]bj/*.json"));
        Assert.True(GitIgnoreFilter.MatchesGlob("src/Obj/deep/file.cs", "**/[Oo]bj/**"));
    }

    [Fact]
    public void MatchesGlob_UnmatchedBracket_IsTreatedAsLiteral()
    {
        // No closing ']' means this is not a class at all.
        Assert.True(GitIgnoreFilter.MatchesGlob("[abc", "[abc"));
        Assert.False(GitIgnoreFilter.MatchesGlob("a", "[abc"));
    }

    [Fact]
    public void MatchesGlob_ClosingBracketAsFirstMember_IsLiteral()
    {
        Assert.True(GitIgnoreFilter.MatchesGlob("]", "[]]"));
    }

    [Fact]
    public void IsIgnored_VisualStudioObjDirectory_IsExcluded()
    {
        var filter = new GitIgnoreFilter();
        filter.AddPatterns(["[Oo]bj/", "[Bb]in/"]);

        Assert.True(filter.IsIgnored("src/obj", isDirectory: true));
        Assert.True(filter.IsIgnored("src/Obj", isDirectory: true));
        Assert.True(filter.IsIgnored("src/bin", isDirectory: true));
        Assert.False(filter.IsIgnored("src/Models", isDirectory: true));
    }

    [Theory]
    [InlineData(".puml")]
    [InlineData(".plantuml")]
    [InlineData(".mmd")]
    [InlineData(".dot")]
    [InlineData(".pdf")]
    [InlineData(".md")]
    public void FileExtensions_DiagramAndDocFormats_AreSupported(string extension)
    {
        Assert.True(LeannMcp.Models.FileExtensions.IsSupported(extension));
    }
}
