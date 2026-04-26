using LeannMcp.Services.Search;
using Xunit;

namespace LeannMcp.Tests;

public sealed class CamelCaseTokenizerTests
{
    [Fact]
    public void Tokenize_CamelCaseIdentifier_EmitsWholeAndComponents()
    {
        var tokens = CamelCaseTokenizer.Tokenize("ThumbnailURICacheExpirationHours");

        Assert.Contains("thumbnailuricacheexpirationhours", tokens);
        Assert.Contains("thumbnail", tokens);
        Assert.Contains("uri", tokens);
        Assert.Contains("cache", tokens);
        Assert.Contains("expiration", tokens);
        Assert.Contains("hours", tokens);
    }

    [Fact]
    public void Tokenize_SnakeCase_EmitsWholeAndComponents()
    {
        var tokens = CamelCaseTokenizer.Tokenize("thumbnail_cache_hours");

        Assert.Contains("thumbnail_cache_hours", tokens);
        Assert.Contains("thumbnail", tokens);
        Assert.Contains("cache", tokens);
        Assert.Contains("hours", tokens);
    }

    [Fact]
    public void Tokenize_DottedPath_EmitsWholeAndComponents()
    {
        var tokens = CamelCaseTokenizer.Tokenize("Hyland.Healthcare.Config");

        Assert.Contains("hyland.healthcare.config", tokens);
        Assert.Contains("hyland", tokens);
        Assert.Contains("healthcare", tokens);
        Assert.Contains("config", tokens);
    }

    [Fact]
    public void Tokenize_MixedSentence_EmitsAllExpectedTokens()
    {
        var tokens = CamelCaseTokenizer.Tokenize(
            "The ThumbnailURICacheExpirationHours setting controls cache life.");

        Assert.Contains("the", tokens);
        Assert.Contains("setting", tokens);
        Assert.Contains("controls", tokens);
        Assert.Contains("cache", tokens);
        Assert.Contains("life", tokens);
        Assert.Contains("thumbnailuricacheexpirationhours", tokens);
        Assert.Contains("thumbnail", tokens);
        Assert.Contains("uri", tokens);
        Assert.Contains("expiration", tokens);
        Assert.Contains("hours", tokens);
    }

    [Fact]
    public void Tokenize_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Empty(CamelCaseTokenizer.Tokenize(""));
        Assert.Empty(CamelCaseTokenizer.Tokenize("   \n\t  "));
    }

    [Fact]
    public void TokenizeWithKinds_FlagsCompoundsAndLeavesPlainWordsUnflagged()
    {
        var tagged = CamelCaseTokenizer.TokenizeWithKinds(
            "ThumbnailURICacheExpirationHours configuration kebab-case my_var");

        var byTerm = tagged.ToDictionary(t => t.Term, t => t.IsWholeIdentifier);

        // Whole CamelCase identifier flagged true; its components flagged false.
        Assert.True(byTerm["thumbnailuricacheexpirationhours"]);
        Assert.False(byTerm["thumbnail"]);
        Assert.False(byTerm["uri"]);

        // Plain English word — decomposes into itself only — NOT flagged.
        Assert.False(byTerm["configuration"]);

        // kebab-case compound: whole flagged true; parts flagged false.
        Assert.True(byTerm["kebab-case"]);
        Assert.False(byTerm["kebab"]);
        Assert.False(byTerm["case"]);

        // snake_case compound: whole flagged true.
        Assert.True(byTerm["my_var"]);
        Assert.False(byTerm["my"]);
        Assert.False(byTerm["var"]);
    }
}
