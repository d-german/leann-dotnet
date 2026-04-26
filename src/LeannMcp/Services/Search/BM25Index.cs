using CSharpFunctionalExtensions;
using LeannMcp.Models;

namespace LeannMcp.Services.Search;

/// <summary>
/// In-memory BM25 ranker over an <see cref="IPassageStore"/>. Built once at
/// index load time and queried per search request. Uses the standard BM25
/// formula with <c>k1 = 1.5</c> and <c>b = 0.75</c>, IDF computed as
/// <c>log((N − df + 0.5) / (df + 0.5) + 1)</c> (the "+1" inside the log keeps
/// the value strictly non-negative even for terms that appear in every doc).
/// <para/>
/// Tokenization is delegated to <see cref="CamelCaseTokenizer"/> so that
/// CamelCase / snake_case / dotted identifiers are indexed both as a whole
/// and as their sub-components. This is what gives the hybrid pipeline
/// recall on exact-identifier queries that pure dense retrievers cannot
/// answer (see T24 diagnostic data).
/// </summary>
public sealed class BM25Index
{
    private const float K1 = 1.5f;
    private const float B = 0.75f;

    /// <summary>
    /// Multiplier applied to a query term's IDF contribution when the term is
    /// the whole-form of a compound identifier (CamelCase / snake / dotted /
    /// kebab). Rewards exact-identifier query matches over partial-component
    /// matches so e.g. a doc containing `ThumbnailURICacheExpirationHours`
    /// once outranks a doc that mentions `Thumbnail` five times when the
    /// query is the full identifier. Pure query-side; no reindex required.
    /// </summary>
    private const float IdentifierBoost = 3.0f;

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> _postings;
    private readonly IReadOnlyDictionary<string, float> _idf;
    private readonly IReadOnlyDictionary<string, int> _docLengths;
    private readonly float _avgDocLength;

    public int Count { get; }

    public BM25Index(IEnumerable<PassageData> passages)
    {
        var df = new Dictionary<string, int>(StringComparer.Ordinal);
        var postings = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var docLengths = new Dictionary<string, int>(StringComparer.Ordinal);
        long totalLength = 0;
        var docCount = 0;

        foreach (var passage in passages)
        {
            var perDoc = IndexPassage(passage, postings, df);
            docLengths[passage.Id] = perDoc;
            totalLength += perDoc;
            docCount++;
        }

        Count = docCount;
        _avgDocLength = docCount == 0 ? 0f : (float)totalLength / docCount;
        _docLengths = docLengths;
        _postings = postings.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyDictionary<string, int>)kv.Value,
            StringComparer.Ordinal);
        _idf = ComputeIdf(df, docCount);
    }

    private static int IndexPassage(
        PassageData passage,
        Dictionary<string, Dictionary<string, int>> postings,
        Dictionary<string, int> df)
    {
        var tokens = CamelCaseTokenizer.Tokenize(passage.Text);
        var termFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in tokens)
            termFrequency[token] = termFrequency.GetValueOrDefault(token) + 1;

        foreach (var (term, tf) in termFrequency)
        {
            if (!postings.TryGetValue(term, out var posting))
            {
                posting = new Dictionary<string, int>(StringComparer.Ordinal);
                postings[term] = posting;
            }
            posting[passage.Id] = tf;
            df[term] = df.GetValueOrDefault(term) + 1;
        }
        return tokens.Count;
    }

    private static IReadOnlyDictionary<string, float> ComputeIdf(
        Dictionary<string, int> df, int docCount)
    {
        var idf = new Dictionary<string, float>(df.Count, StringComparer.Ordinal);
        foreach (var (term, frequency) in df)
            idf[term] = (float)Math.Log((docCount - frequency + 0.5) / (frequency + 0.5) + 1.0);
        return idf;
    }

    public IReadOnlyList<(string Id, float Score)> Search(string query, int topK)
    {
        if (Count == 0 || string.IsNullOrWhiteSpace(query) || topK <= 0)
            return Array.Empty<(string, float)>();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var scores = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var qt in CamelCaseTokenizer.TokenizeWithKinds(query))
        {
            if (!seen.Add(qt.Term)) continue;
            var boost = qt.IsWholeIdentifier ? IdentifierBoost : 1.0f;
            AccumulateTermScores(qt.Term, scores, boost);
        }

        return scores
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(topK)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    private void AccumulateTermScores(string term, Dictionary<string, float> scores, float boost)
    {
        if (!_postings.TryGetValue(term, out var posting)) return;
        if (!_idf.TryGetValue(term, out var termIdf)) return;

        var idfBoosted = termIdf * boost;
        foreach (var (docId, tf) in posting)
        {
            var lenNorm = 1f - B + B * (_docLengths[docId] / _avgDocLength);
            var contribution = idfBoosted * (tf * (K1 + 1)) / (tf + K1 * lenNorm);
            scores[docId] = scores.GetValueOrDefault(docId) + contribution;
        }
    }

    /// <summary>
    /// Maximum document frequency for a whole-identifier query token to be
    /// considered a pin candidate. T27 used df=1 (strictly unambiguous) but
    /// real PDFs include passing references to the same identifier (e.g. a
    /// "see also" mention or cross-reference), pushing df to 2-3 for terms
    /// that still have a single canonical home. df ≤ 5 covers those cases
    /// while still excluding genuinely common identifiers where the pin
    /// would be unsafe.
    /// </summary>
    private const int MaxIdentifierDf = 5;

    /// <summary>
    /// Returns <c>Some(docId)</c> for the doc with the highest full-query
    /// BM25 score among all documents that contain any whole-identifier
    /// token from the query whose document frequency is ≤
    /// <see cref="MaxIdentifierDf"/>. Returns <see cref="Maybe.None"/> when
    /// the query has no qualifying whole-identifier tokens (natural-language
    /// queries) or when every such token has df &gt; threshold (super-common
    /// identifiers — pin would be unsafe). The chosen doc is scored using
    /// the full BM25 ranking over the entire query, not just the identifier
    /// term, so cross-term evidence shapes which low-df-identifier doc wins.
    /// <para/>
    /// Powers the hybrid-retrieval pin: when the user clearly wants an
    /// identifier lookup, BM25 ordering is the authoritative signal and we
    /// promote BM25's argmax over the candidate set to rank #1, bypassing
    /// the dense/RRF noise that erases identifier ranking on Contriever.
    /// </summary>
    public Maybe<string> FindBestIdentifierMatch(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Maybe<string>.None;

        var candidates = new HashSet<string>(StringComparer.Ordinal);
        var seenTerms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var qt in CamelCaseTokenizer.TokenizeWithKinds(query))
        {
            if (!qt.IsWholeIdentifier) continue;
            if (!seenTerms.Add(qt.Term)) continue;
            if (!_postings.TryGetValue(qt.Term, out var posting)) continue;
            if (posting.Count > MaxIdentifierDf) continue;
            foreach (var docId in posting.Keys) candidates.Add(docId);
        }

        if (candidates.Count == 0) return Maybe<string>.None;

        var best = Search(query, topK: Count)
            .Where(h => candidates.Contains(h.Id))
            .Select(h => h.Id)
            .FirstOrDefault();

        return best is null ? Maybe<string>.None : Maybe<string>.From(best);
    }
}
