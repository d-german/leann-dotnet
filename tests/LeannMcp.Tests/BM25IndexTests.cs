using LeannMcp.Models;
using LeannMcp.Services.Search;
using Xunit;

namespace LeannMcp.Tests;

public sealed class BM25IndexTests
{
    private static PassageData P(string id, string text) => new(id, text, null);

    [Fact]
    public void Search_TermFrequency_RanksDocWithMoreOccurrencesHigher()
    {
        var index = new BM25Index(new[]
        {
            P("a", "cache cache cache cache cache settings overview"),
            P("b", "cache settings overview documentation introduction"),
            P("c", "completely unrelated content about installation steps"),
        });

        var hits = index.Search("cache", topK: 3);

        Assert.Equal("a", hits[0].Id);
        Assert.Equal("b", hits[1].Id);
    }

    [Fact]
    public void Search_IdfDownWeightsCommonTerms()
    {
        // "the" appears in every doc, "thumbnail" only in one — query for both
        // should rank the doc containing "thumbnail" first.
        var index = new BM25Index(new[]
        {
            P("a", "the the the introduction the section"),
            P("b", "the the the architecture the diagram"),
            P("c", "the thumbnail uri cache expiration hours setting"),
        });

        var hits = index.Search("the thumbnail", topK: 3);

        Assert.Equal("c", hits[0].Id);
    }

    [Fact]
    public void Search_ExactCamelCaseIdentifier_SurfacesUniqueChunkAtRankOne()
    {
        // The acceptance scenario from T24: query matches an exact CamelCase
        // identifier appearing in only one passage.
        var index = new BM25Index(new[]
        {
            P("noise1", "patient banner missing data groups configuration"),
            P("noise2", "patient variable list age birthDate gender medicalRecord"),
            P("target", "ThumbnailURICacheExpirationHours determines how long cached URIs persist"),
            P("noise3", "full text search description supports prefix queries"),
            P("noise4", "session inactivity timeout settings reset interval"),
        });

        var hits = index.Search("ThumbnailURICacheExpirationHours", topK: 5);

        Assert.NotEmpty(hits);
        Assert.Equal("target", hits[0].Id);
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        var index = new BM25Index(new[] { P("a", "anything") });
        Assert.Empty(index.Search("", topK: 5));
        Assert.Empty(index.Search("   ", topK: 5));
    }

    [Fact]
    public void Search_IdentifierBoost_OutranksHighFrequencyComponentMatches()
    {
        // Regression test for the live T25 probe-2 failure mode:
        //   doc A spams 'Thumbnail' five times but lacks the unbroken identifier;
        //   doc B contains the unbroken identifier ONCE plus each component once.
        // Without the IdentifierBoost, BM25's TF saturation gives doc A more
        // total contribution from the 'thumbnail' term and it wins. With the
        // boost, the high-IDF whole-identifier match dominates and doc B wins.
        var index = new BM25Index(new[]
        {
            P("a", "Thumbnail thumbnail Thumbnail thumbnail Thumbnail rendering and " +
                   "preview generation pipeline overview details"),
            P("b", "ThumbnailURICacheExpirationHours sets the URI cache expiration " +
                   "hours used by the thumbnail subsystem"),
        });

        var hits = index.Search("ThumbnailURICacheExpirationHours", topK: 2);

        Assert.Equal("b", hits[0].Id);
    }

    [Fact]
    public void FindBestIdentifierMatch_DfEqualsOne_ReturnsThatDoc()
    {
        var index = new BM25Index(new[]
        {
            P("noise1", "patient banner missing data groups configuration"),
            P("target", "ThumbnailURICacheExpirationHours determines URI expiration"),
            P("noise2", "session inactivity timeout reset behaviour"),
        });

        var pin = index.FindBestIdentifierMatch("ThumbnailURICacheExpirationHours");

        Assert.True(pin.HasValue);
        Assert.Equal("target", pin.Value);
    }

    [Fact]
    public void FindBestIdentifierMatch_NaturalLanguageQuery_ReturnsNone()
    {
        var index = new BM25Index(new[]
        {
            P("a", "thumbnail cache expiration settings"),
            P("b", "session inactivity timeout"),
        });

        var pin = index.FindBestIdentifierMatch("thumbnail cache expiration");

        Assert.False(pin.HasValue);
    }

    [Fact]
    public void FindBestIdentifierMatch_DfTwo_PicksDocWithHigherBm25Score()
    {
        // Reproduces the live mrg failure mode: identifier appears in two
        // chunks (the canonical paragraph + a passing reference). T27's strict
        // df=1 rule refused to pin; T28's relaxation picks the canonical doc
        // by full-query BM25 score (higher TF, term-density wins).
        var index = new BM25Index(new[]
        {
            P("competitor",
                "ContentListConfiguration Column. Set Enabled for the Thumbnail " +
                "property to False. See ThumbnailURICacheExpirationHours for the " +
                "related cache lifetime setting. Other thumbnail properties also apply."),
            P("target",
                "ThumbnailURICacheExpirationHours determines how long the BFF " +
                "caches generated thumbnail URIs before refreshing them. " +
                "ThumbnailURICacheExpirationHours is read at startup."),
            P("noise", "unrelated session timeout configuration"),
        });

        var pin = index.FindBestIdentifierMatch("ThumbnailURICacheExpirationHours");

        Assert.True(pin.HasValue);
        Assert.Equal("target", pin.Value);
    }

    [Fact]
    public void FindBestIdentifierMatch_DfAboveThreshold_ReturnsNone()
    {
        // Identifier appears in 6 chunks (above the df<=5 threshold) → too
        // common to safely pin → fall through to normal RRF.
        var passages = Enumerable.Range(0, 6)
            .Select(i => P($"d{i}", $"context {i} CommonHelperUtility section"))
            .Append(P("noise", "unrelated content"))
            .ToArray();
        var index = new BM25Index(passages);

        var pin = index.FindBestIdentifierMatch("CommonHelperUtility");

        Assert.False(pin.HasValue);
    }
}
