using System.Text.RegularExpressions;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Post-processes chunks to remove low-signal content that pollutes embedding search results.
/// Drops chunks that are: too short, dominated by punctuation/braces, contain large base64
/// blobs (embedded images), or contain long underscore runs (generated IDL artifacts).
///
/// This is the second-line defense: even AST-aware chunkers can produce noise when source
/// files contain inlined base64 images (e.g. data URIs in <c>.cs</c> files) or generated
/// boilerplate. The filter is purely heuristic and intentionally conservative.
/// </summary>
public static class ChunkQualityFilter
{
    private const int MinTrimmedLength = 20;
    private const double MaxPunctuationRatio = 0.70;
    private const int MaxBase64RunLength = 200;
    private const int MaxUnderscoreRunLength = 10;

    private static readonly HashSet<char> PunctuationChars =
        new("{}();,[]<>:.|&!?+-*/=^%~@#$\"'`\\");

    private static readonly Regex Base64RunRegex =
        new(@"[A-Za-z0-9+/]{" + MaxBase64RunLength + ",}", RegexOptions.Compiled);

    private static readonly Regex UnderscoreRunRegex =
        new("_{" + (MaxUnderscoreRunLength + 1) + ",}", RegexOptions.Compiled);

    public static IReadOnlyList<string> Filter(IReadOnlyList<string> chunks)
    {
        if (chunks.Count == 0) return chunks;
        return chunks.Where(IsAcceptable).ToList();
    }

    public static bool IsAcceptable(string chunk)
    {
        if (string.IsNullOrWhiteSpace(chunk)) return false;
        var trimmed = chunk.Trim();
        if (trimmed.Length < MinTrimmedLength) return false;
        if (PunctuationRatio(trimmed) > MaxPunctuationRatio) return false;
        if (Base64RunRegex.IsMatch(trimmed)) return false;
        if (UnderscoreRunRegex.IsMatch(trimmed)) return false;
        return true;
    }

    private static double PunctuationRatio(string text)
    {
        var nonWhitespace = 0;
        var punctuation = 0;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) continue;
            nonWhitespace++;
            if (PunctuationChars.Contains(c)) punctuation++;
        }
        return nonWhitespace == 0 ? 0.0 : (double)punctuation / nonWhitespace;
    }
}
