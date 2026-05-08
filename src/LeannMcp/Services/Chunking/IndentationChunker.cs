using LeannMcp.Models;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Indentation-aware chunker for Python and other whitespace-scoped languages.
/// Detects top-level blocks by tracking indentation: a return to column 0 after
/// indented lines signals a block boundary. Decorators (<c>@foo</c>) at column 0
/// are grouped with the following <c>def</c>/<c>class</c>. Triple-quoted strings
/// are tracked so that column-0 text inside them does not cause false boundaries.
///
/// When a single top-level block exceeds <see cref="ChunkingOptions.CodeChunkSize"/>,
/// it is split at the next dedent point (where indentation decreases). If no dedent
/// yields a sub-chunk under the limit, the block falls back to line-based splitting.
/// </summary>
public sealed class IndentationChunker : ICodeChunkStrategy
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "python",
    };

    public bool CanHandle(string? language) => language is not null && Supported.Contains(language);

    public IReadOnlyList<string> Chunk(string content, ChunkingOptions options)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [];

        var blocks = ExtractTopLevelBlocks(content);
        return ApplySizeLimit(blocks, options.CodeChunkSize);
    }

    /// <summary>
    /// Walks source lines and groups them into top-level blocks. A block starts at
    /// a non-blank line with zero indentation and continues until the next such line.
    /// Triple-quoted strings are tracked to avoid false boundaries.
    /// </summary>
    private static List<string> ExtractTopLevelBlocks(string src)
    {
        var lines = src.Split('\n');
        var blocks = new List<string>();
        var currentBlock = new List<string>();
        var inTripleQuote = false;
        var tripleChar = '\0';
        var seenIndented = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var stripped = line.TrimEnd('\r');

            // Track triple-quote state (toggle on odd occurrences of """ or ''').
            inTripleQuote = UpdateTripleQuoteState(stripped, inTripleQuote, ref tripleChar);

            if (inTripleQuote)
            {
                currentBlock.Add(stripped);
                continue;
            }

            var indent = MeasureIndent(stripped);
            var isBlankOrComment = IsBlankOrComment(stripped);

            if (indent == 0 && !isBlankOrComment && seenIndented)
            {
                // A new top-level block starts here. Flush the previous one.
                FlushBlock(blocks, currentBlock);
                currentBlock.Clear();
                seenIndented = false;
            }

            currentBlock.Add(stripped);

            if (indent > 0)
                seenIndented = true;
        }

        FlushBlock(blocks, currentBlock);
        return blocks;
    }

    /// <summary>
    /// Splits any block that exceeds <paramref name="maxSize"/> at dedent boundaries.
    /// Falls back to simple line-based splitting when no dedent produces a small-enough chunk.
    /// </summary>
    private static IReadOnlyList<string> ApplySizeLimit(List<string> blocks, int maxSize)
    {
        var result = new List<string>();

        foreach (var block in blocks)
        {
            if (block.Length <= maxSize)
            {
                result.Add(block);
                continue;
            }

            var subChunks = SplitAtDedent(block, maxSize);
            result.AddRange(subChunks);
        }

        return result;
    }

    /// <summary>
    /// Splits a single oversized block at dedent boundaries (lines where indentation
    /// decreases relative to the previous non-blank line). If the first sub-chunk still
    /// exceeds <paramref name="maxSize"/>, falls back to grouping lines by size.
    /// </summary>
    private static List<string> SplitAtDedent(string block, int maxSize)
    {
        var lines = block.Split('\n');
        var chunks = new List<string>();
        var current = new List<string>();
        var currentLen = 0;
        var prevIndent = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var indent = MeasureIndent(line);
            var isBlank = string.IsNullOrWhiteSpace(line);
            var lineLen = line.Length + 1; // +1 for the newline we'll rejoin with

            // Dedent detected: indentation decreased and we'd exceed the limit.
            if (!isBlank && prevIndent > 0 && indent < prevIndent && currentLen + lineLen > maxSize && current.Count > 0)
            {
                chunks.Add(JoinLines(current));
                current.Clear();
                currentLen = 0;
            }

            current.Add(line);
            currentLen += lineLen;

            if (!isBlank)
                prevIndent = indent;
        }

        if (current.Count > 0)
            chunks.Add(JoinLines(current));

        // If any chunk is still oversized, fall back to simple line-based splitting.
        var final = new List<string>();
        foreach (var chunk in chunks)
        {
            if (chunk.Length <= maxSize)
            {
                final.Add(chunk);
            }
            else
            {
                final.AddRange(SplitByLines(chunk, maxSize));
            }
        }

        return final;
    }

    /// <summary>
    /// Last-resort splitter: groups lines into chunks that don't exceed <paramref name="maxSize"/>.
    /// </summary>
    private static List<string> SplitByLines(string block, int maxSize)
    {
        var lines = block.Split('\n');
        var chunks = new List<string>();
        var current = new List<string>();
        var currentLen = 0;

        foreach (var line in lines)
        {
            var lineLen = line.Length + 1;

            if (currentLen + lineLen > maxSize && current.Count > 0)
            {
                chunks.Add(JoinLines(current));
                current.Clear();
                currentLen = 0;
            }

            current.Add(line);
            currentLen += lineLen;
        }

        if (current.Count > 0)
            chunks.Add(JoinLines(current));

        return chunks;
    }

    /// <summary>
    /// Toggles triple-quote state by counting non-overlapping occurrences of <c>"""</c>
    /// or <c>'''</c> on a single line. An odd count toggles the state.
    /// </summary>
    private static bool UpdateTripleQuoteState(string line, bool currentlyInTriple, ref char tripleChar)
    {
        var inTriple = currentlyInTriple;

        for (var i = 0; i <= line.Length - 3; i++)
        {
            var c = line[i];

            if (!inTriple && (c == '"' || c == '\'') && line[i + 1] == c && line[i + 2] == c)
            {
                inTriple = true;
                tripleChar = c;
                i += 2; // skip past the triple
            }
            else if (inTriple && c == tripleChar && line[i + 1] == c && line[i + 2] == c)
            {
                inTriple = false;
                i += 2;
            }
        }

        return inTriple;
    }

    private static int MeasureIndent(string line)
    {
        var count = 0;
        foreach (var c in line)
        {
            if (c == ' ') count++;
            else if (c == '\t') count += 4;
            else break;
        }

        return count;
    }

    private static bool IsBlankOrComment(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length == 0 || trimmed.StartsWith('#');
    }

    private static void FlushBlock(List<string> blocks, List<string> lines)
    {
        if (lines.Count == 0) return;
        var text = JoinLines(lines).Trim();
        if (text.Length > 0)
            blocks.Add(text);
    }

    private static string JoinLines(List<string> lines) => string.Join('\n', lines);
}
