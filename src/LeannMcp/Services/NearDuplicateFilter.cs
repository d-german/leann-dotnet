using System.Numerics.Tensors;

namespace LeannMcp.Services;

/// <summary>
/// Pure helper that filters near-duplicate vector-search hits.
/// <para/>
/// Iterates a score-sorted hit list in descending order and keeps each hit
/// whose maximum cosine similarity against any already-kept hit is below
/// <c>threshold</c>. Designed as a post-processing step layered on top of an
/// arbitrary <see cref="IVectorIndex"/> — the index itself is not aware of
/// dedup. Operates on already-L2-normalized vectors (which is the invariant
/// upheld by <see cref="FlatVectorIndex"/>) so cosine similarity = dot product.
/// <para/>
/// User-reported defect D2 (heavy near-duplicates in top-K) was the dominant
/// cause of the mrg index quality regression: results 1 &amp; 9 / 3 &amp; 8 / 5
/// &amp; 7 of the same query were essentially the same passage. This filter is
/// the retrieval-side safety net; chunking-side fixes (T05/T07/T09) reduce
/// duplicates at write time.
/// </summary>
internal static class NearDuplicateFilter
{
    /// <summary>Default similarity threshold above which a hit is treated as a near-duplicate.</summary>
    public const double DefaultThreshold = 0.95;

    /// <summary>Default over-fetch multiplier — request this many times <c>topK</c> from the index.</summary>
    public const int DefaultOverFetchFactor = 3;

    /// <summary>
    /// Filters <paramref name="hits"/> in score-descending order, keeping at
    /// most <paramref name="topK"/> entries with pairwise cosine similarity
    /// strictly below <paramref name="threshold"/>. <paramref name="getEmbedding"/>
    /// supplies the L2-normalized vector for a given id; if it returns null
    /// (id missing or vector unavailable), the hit is kept without comparison
    /// to avoid silently dropping results.
    /// <para/>
    /// Setting <paramref name="threshold"/> to 0 or 1 disables filtering and
    /// returns the first <paramref name="topK"/> hits unchanged (backwards
    /// compatibility with callers that opt out).
    /// </summary>
    public static IReadOnlyList<(string Id, float Score)> Filter(
        IReadOnlyList<(string Id, float Score)> hits,
        Func<string, float[]?> getEmbedding,
        int topK,
        double threshold = DefaultThreshold)
    {
        if (topK <= 0) return Array.Empty<(string, float)>();
        if (threshold <= 0.0 || threshold >= 1.0) return Take(hits, topK);
        if (hits.Count == 0) return hits;

        var keptVectors = new List<float[]>(topK);
        var keptHits = new List<(string Id, float Score)>(topK);

        foreach (var hit in hits)
        {
            if (keptHits.Count == topK) break;

            var vec = getEmbedding(hit.Id);
            if (vec is null || IsNovelEnough(vec, keptVectors, threshold))
            {
                keptHits.Add(hit);
                if (vec is not null) keptVectors.Add(vec);
            }
        }

        return keptHits;
    }

    private static bool IsNovelEnough(float[] candidate, List<float[]> kept, double threshold)
    {
        foreach (var k in kept)
        {
            // Vectors are pre-normalized → dot product == cosine similarity.
            var sim = TensorPrimitives.Dot(candidate.AsSpan(), k.AsSpan());
            if (sim >= threshold) return false;
        }
        return true;
    }

    private static IReadOnlyList<(string Id, float Score)> Take(
        IReadOnlyList<(string Id, float Score)> hits, int topK)
    {
        if (hits.Count <= topK) return hits;
        var result = new (string, float)[topK];
        for (var i = 0; i < topK; i++) result[i] = hits[i];
        return result;
    }
}
