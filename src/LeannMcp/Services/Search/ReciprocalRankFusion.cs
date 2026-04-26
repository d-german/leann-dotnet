namespace LeannMcp.Services.Search;

/// <summary>
/// Reciprocal Rank Fusion — combines two ranked result lists into one
/// without requiring their scores to live in comparable ranges. For each
/// document <c>id</c>, the fused score is
/// <c>Σ 1 / (k + rank_in_list)</c> over each list it appears in
/// (rank is 1-based; <c>k</c> defaults to 60, the value from the original
/// Cormack et al. paper). This is the standard fusion algorithm used in
/// hybrid dense + sparse retrieval pipelines because it is parameter-light,
/// scale-invariant, and outperforms most weighted-sum schemes in practice.
/// </summary>
public static class ReciprocalRankFusion
{
    public const int DefaultK = 60;

    public static IReadOnlyList<(string Id, float Score)> Fuse(
        IReadOnlyList<(string Id, float Score)> dense,
        IReadOnlyList<(string Id, float Score)> lexical,
        int topK,
        int k = DefaultK,
        float lexicalWeight = 1.0f)
    {
        var scores = new Dictionary<string, float>(StringComparer.Ordinal);
        AccumulateRanks(dense, scores, k, weight: 1.0f);
        AccumulateRanks(lexical, scores, k, weight: lexicalWeight);

        return scores
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(topK)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    private static void AccumulateRanks(
        IReadOnlyList<(string Id, float Score)> ranked,
        Dictionary<string, float> scores,
        int k,
        float weight)
    {
        for (var i = 0; i < ranked.Count; i++)
        {
            var contribution = weight / (k + i + 1);
            var id = ranked[i].Id;
            scores[id] = scores.GetValueOrDefault(id) + contribution;
        }
    }
}
