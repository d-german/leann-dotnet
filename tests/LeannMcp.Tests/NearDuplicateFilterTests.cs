using LeannMcp.Services;
using Xunit;

namespace LeannMcp.Tests;

/// <summary>
/// Unit tests for <see cref="NearDuplicateFilter"/> — the retrieval-time safety
/// net that drops adjacent top-K results whose cosine similarity exceeds the
/// configured threshold. Each test constructs deterministic L2-normalized
/// vectors so cosine == dot product and the assertions are exact.
/// </summary>
public class NearDuplicateFilterTests
{
    [Fact]
    public void Filter_DropsNearDuplicate_WhenSimilarityAboveThreshold()
    {
        var v1 = Normalize(1f, 0f, 0f);
        // v2 differs from v1 by ~1° — cosine ≈ 0.9998, well above 0.95
        var v2 = Normalize(0.9999f, 0.01f, 0f);
        var v3 = Normalize(0f, 1f, 0f);

        var hits = new (string, float)[] { ("a", 0.99f), ("b", 0.98f), ("c", 0.80f) };
        var lookup = MakeLookup(("a", v1), ("b", v2), ("c", v3));

        var result = NearDuplicateFilter.Filter(hits, lookup, topK: 5, threshold: 0.95);

        Assert.Equal(2, result.Count);
        Assert.Equal("a", result[0].Id);
        Assert.Equal("c", result[1].Id);
    }

    [Fact]
    public void Filter_KeepsAllResults_WhenThresholdDisabled()
    {
        var v1 = Normalize(1f, 0f);
        var v2 = Normalize(0.9999f, 0.01f);
        var hits = new (string, float)[] { ("a", 0.99f), ("b", 0.98f) };
        var lookup = MakeLookup(("a", v1), ("b", v2));

        var disabledZero = NearDuplicateFilter.Filter(hits, lookup, topK: 5, threshold: 0.0);
        var disabledOne = NearDuplicateFilter.Filter(hits, lookup, topK: 5, threshold: 1.0);

        Assert.Equal(2, disabledZero.Count);
        Assert.Equal(2, disabledOne.Count);
    }

    [Fact]
    public void Filter_TruncatesToTopK_AfterDeduplication()
    {
        var orthogonal = new[]
        {
            Normalize(1f, 0f, 0f, 0f),
            Normalize(0f, 1f, 0f, 0f),
            Normalize(0f, 0f, 1f, 0f),
            Normalize(0f, 0f, 0f, 1f),
        };
        var hits = new (string, float)[]
        {
            ("a", 0.9f), ("b", 0.8f), ("c", 0.7f), ("d", 0.6f),
        };
        var lookup = MakeLookup(("a", orthogonal[0]), ("b", orthogonal[1]),
                                ("c", orthogonal[2]), ("d", orthogonal[3]));

        var result = NearDuplicateFilter.Filter(hits, lookup, topK: 2, threshold: 0.95);

        Assert.Equal(2, result.Count);
        Assert.Equal("a", result[0].Id);
        Assert.Equal("b", result[1].Id);
    }

    [Fact]
    public void Filter_KeepsHit_WhenEmbeddingMissing()
    {
        // If the lookup returns null we can't compare → keep the hit rather
        // than silently drop a possibly-good result.
        var hits = new (string, float)[] { ("a", 0.9f), ("missing", 0.85f) };
        var v1 = Normalize(1f, 0f);
        var lookup = MakeLookup(("a", v1));

        var result = NearDuplicateFilter.Filter(hits, lookup, topK: 5, threshold: 0.95);

        Assert.Equal(2, result.Count);
        Assert.Equal("a", result[0].Id);
        Assert.Equal("missing", result[1].Id);
    }

    [Fact]
    public void Filter_PreservesOriginalOrder_AmongKeptHits()
    {
        // Keep score-descending order even after some hits are dropped.
        var v1 = Normalize(1f, 0f, 0f);
        var v2 = Normalize(0.9999f, 0.01f, 0f);
        var v3 = Normalize(0f, 1f, 0f);
        var v4 = Normalize(0.0001f, 0.9999f, 0f);
        var v5 = Normalize(0f, 0f, 1f);

        var hits = new (string, float)[]
        {
            ("a", 0.9f), ("b-dup-of-a", 0.85f),
            ("c", 0.80f), ("d-dup-of-c", 0.75f),
            ("e", 0.70f),
        };
        var lookup = MakeLookup(("a", v1), ("b-dup-of-a", v2), ("c", v3), ("d-dup-of-c", v4), ("e", v5));

        var result = NearDuplicateFilter.Filter(hits, lookup, topK: 5, threshold: 0.95);

        Assert.Equal(new[] { "a", "c", "e" }, result.Select(r => r.Id).ToArray());
    }

    [Fact]
    public void Filter_ReturnsEmpty_WhenTopKIsZero()
    {
        var hits = new (string, float)[] { ("a", 0.9f) };
        var v1 = Normalize(1f, 0f);
        var lookup = MakeLookup(("a", v1));

        var result = NearDuplicateFilter.Filter(hits, lookup, topK: 0, threshold: 0.95);

        Assert.Empty(result);
    }

    private static float[] Normalize(params float[] components)
    {
        var norm = MathF.Sqrt(components.Sum(c => c * c));
        return components.Select(c => c / norm).ToArray();
    }

    private static Func<string, float[]?> MakeLookup(params (string Id, float[] Vec)[] entries)
    {
        var dict = entries.ToDictionary(e => e.Id, e => e.Vec, StringComparer.Ordinal);
        return id => dict.TryGetValue(id, out var v) ? v : null;
    }
}
