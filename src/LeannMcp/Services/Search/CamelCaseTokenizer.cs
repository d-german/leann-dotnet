using System.Text.RegularExpressions;

namespace LeannMcp.Services.Search;

/// <summary>
/// Lossless tokenizer for technical-document BM25 indexing. Emits, for each
/// raw whitespace-delimited word, BOTH the lowercased whole token AND its
/// CamelCase / snake_case / dotted-path / kebab-case sub-components.
/// <para/>
/// Example: <c>"ThumbnailURICacheExpirationHours"</c> →
/// <c>["thumbnailuricacheexpirationhours", "thumbnail", "uri", "cache",
/// "expiration", "hours"]</c>.
/// <para/>
/// This dual-emission is the linchpin of hybrid retrieval over technical
/// documentation: it lets the BM25 ranker score both exact-identifier
/// queries (which a dense embedder fragments into meaningless subwords) and
/// natural-language queries that mention only some component words.
/// </summary>
public static class CamelCaseTokenizer
{
    private static readonly Regex WordSplit =
        new(@"[^\w\.\-]+", RegexOptions.Compiled);

    private static readonly Regex SubSplit =
        new(@"[_\.\-]+", RegexOptions.Compiled);

    private static readonly Regex CamelSplit =
        new(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])",
            RegexOptions.Compiled);

    private const int MinTokenLength = 2;

    public static IReadOnlyList<string> Tokenize(string text)
    {
        var tagged = TokenizeWithKinds(text);
        if (tagged.Count == 0) return Array.Empty<string>();
        var bare = new List<string>(tagged.Count);
        foreach (var t in tagged) bare.Add(t.Term);
        return bare;
    }

    /// <summary>
    /// Like <see cref="Tokenize"/> but tags each emitted token with whether it
    /// is the whole form of a real compound identifier (CamelCase, snake_case,
    /// dotted, kebab-case) — i.e., a word whose component-expansion produced
    /// at least one sub-token different from the whole-form. Plain English
    /// words like <c>configuration</c> decompose into themselves only and are
    /// NOT flagged. Used by <see cref="BM25Index"/> to apply an IDF boost on
    /// whole-identifier query terms so exact-identifier queries decisively
    /// rank the chunk containing the unbroken token at position #1.
    /// </summary>
    public static IReadOnlyList<TokenizedTerm> TokenizeWithKinds(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<TokenizedTerm>();

        var tokens = new List<TokenizedTerm>();
        foreach (var word in WordSplit.Split(text))
        {
            if (word.Length < MinTokenLength) continue;
            EmitWord(word, tokens);
        }
        return tokens;
    }

    private static void EmitWord(string word, List<TokenizedTerm> sink)
    {
        var whole = word.ToLowerInvariant();
        var components = ExpandComponents(word);
        var isCompound = components.Count > 0 && !(components.Count == 1 && components.Contains(whole));

        var emitted = new HashSet<string>(StringComparer.Ordinal) { whole };
        sink.Add(new TokenizedTerm(whole, isCompound));
        foreach (var c in components)
        {
            if (emitted.Add(c))
                sink.Add(new TokenizedTerm(c, false));
        }
    }

    private static HashSet<string> ExpandComponents(string word)
    {
        var parts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sub in SubSplit.Split(word))
        {
            if (sub.Length < MinTokenLength) continue;
            foreach (var part in CamelSplit.Split(sub))
                if (part.Length >= MinTokenLength)
                    parts.Add(part.ToLowerInvariant());
        }
        return parts;
    }
}

/// <summary>
/// A single token emitted by <see cref="CamelCaseTokenizer.TokenizeWithKinds"/>.
/// <paramref name="IsWholeIdentifier"/> is true iff the original raw word was a
/// real compound (CamelCase / snake / dotted / kebab) AND this term is the
/// unbroken whole-form. Component sub-tokens always have IsWholeIdentifier=false.
/// </summary>
public readonly record struct TokenizedTerm(string Term, bool IsWholeIdentifier);
