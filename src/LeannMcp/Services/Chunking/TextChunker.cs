namespace LeannMcp.Services.Chunking;

/// <summary>
/// Splits text into overlapping chunks. Supports sentence-based splitting for documents
/// and line-based splitting (with line numbers) for code files.
/// </summary>
public sealed class TextChunker : ITextChunker
{
    public IReadOnlyList<string> ChunkText(string text, int chunkSize, int chunkOverlap, SplitMode mode)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        chunkSize = Math.Max(1, chunkSize);
        chunkOverlap = Math.Clamp(chunkOverlap, 0, chunkSize - 1);

        return mode switch
        {
            SplitMode.CodeLine => ChunkCodeLines(text, chunkSize, chunkOverlap),
            SplitMode.Sentence => ChunkSentences(text, chunkSize, chunkOverlap),
            _ => ChunkSentences(text, chunkSize, chunkOverlap),
        };
    }

    /// <summary>
    /// Splits code: prepends line numbers, then splits at newline boundaries with overlap.
    /// Trims partial first lines from overlap (matching Python behavior).
    /// </summary>
    private static IReadOnlyList<string> ChunkCodeLines(string text, int chunkSize, int chunkOverlap)
    {
        var lines = text.Split('\n');
        var width = lines.Length.ToString().Length;

        // Prepend line numbers: "  1|line content"
        var numberedLines = new string[lines.Length];
        for (var i = 0; i < lines.Length; i++)
            numberedLines[i] = (i + 1).ToString().PadLeft(width) + "|" + lines[i];

        var segments = numberedLines.Select(l => l + "\n").ToArray();
        var chunks = MergeSegmentsWithOverlap(segments, chunkSize, chunkOverlap);

        // Trim partial first lines from overlap
        return chunks.Select(TrimPartialFirstLine).Where(c => c.Length > 0).ToList();
    }

    /// <summary>
    /// Splits text at paragraph boundaries, then sentence boundaries, then word boundaries.
    /// </summary>
    private static IReadOnlyList<string> ChunkSentences(string text, int chunkSize, int chunkOverlap)
    {
        // Level 1: split by paragraphs
        var paragraphs = text.Split("\n\n", StringSplitOptions.None);
        var segments = new List<string>();

        foreach (var para in paragraphs)
        {
            if (para.Length <= chunkSize)
            {
                segments.Add(para + "\n\n");
                continue;
            }

            // Level 2: split long paragraphs by sentences (period + space, or newline)
            foreach (var sentence in SplitBySentenceBoundary(para))
            {
                if (sentence.Length <= chunkSize)
                {
                    segments.Add(sentence);
                    continue;
                }

                // Level 3: split very long sentences by word boundary
                segments.AddRange(SplitByWords(sentence, chunkSize));
            }
        }

        return MergeSegmentsWithOverlap(segments.ToArray(), chunkSize, chunkOverlap);
    }

    /// <summary>
    /// Splits text at sentence boundaries (". ", ".\n", "! ", "? ", or standalone newlines).
    /// </summary>
    private static List<string> SplitBySentenceBoundary(string text)
    {
        var results = new List<string>();
        var start = 0;

        for (var i = 0; i < text.Length - 1; i++)
        {
            var isSentenceEnd = text[i] is '.' or '!' or '?' && (text[i + 1] == ' ' || text[i + 1] == '\n');
            var isNewline = text[i] == '\n';

            if (isSentenceEnd || isNewline)
            {
                var end = isSentenceEnd ? i + 2 : i + 1;
                results.Add(text[start..end]);
                start = end;
            }
        }

        if (start < text.Length)
            results.Add(text[start..]);

        return results;
    }

    /// <summary>
    /// Splits a long string into word-boundary chunks of at most chunkSize characters.
    /// </summary>
    private static List<string> SplitByWords(string text, int chunkSize)
    {
        var results = new List<string>();
        var words = text.Split(' ');
        var current = "";

        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (candidate.Length > chunkSize && current.Length > 0)
            {
                results.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
            results.Add(current);

        return results;
    }

    /// <summary>
    /// Merges small segments into chunks of approximately chunkSize,
    /// with chunkOverlap characters of overlap between consecutive chunks.
    /// </summary>
    private static IReadOnlyList<string> MergeSegmentsWithOverlap(
        string[] segments, int chunkSize, int chunkOverlap)
    {
        if (segments.Length == 0) return [];

        var chunks = new List<string>();
        var currentChunk = "";

        foreach (var segment in segments)
        {
            if (currentChunk.Length + segment.Length > chunkSize && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.TrimEnd());

                // Start next chunk with overlap from end of current
                currentChunk = chunkOverlap > 0 && currentChunk.Length > chunkOverlap
                    ? currentChunk[^chunkOverlap..] + segment
                    : segment;
            }
            else
            {
                currentChunk += segment;
            }
        }

        if (currentChunk.Length > 0)
            chunks.Add(currentChunk.TrimEnd());

        return chunks;
    }

    /// <summary>
    /// For code chunks created with overlap, the first line may be partial
    /// (doesn't start with a digit). Trim it to the next newline.
    /// </summary>
    private static string TrimPartialFirstLine(string chunk)
    {
        var trimmed = chunk.Trim();
        if (trimmed.Length == 0) return "";

        if (char.IsDigit(trimmed[0]) || trimmed[0] == ' ')
            return trimmed;

        var newlineIdx = trimmed.IndexOf('\n');
        return newlineIdx >= 0 ? trimmed[(newlineIdx + 1)..].Trim() : "";
    }
}
