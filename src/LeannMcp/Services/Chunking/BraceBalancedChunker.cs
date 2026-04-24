using LeannMcp.Models;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Brace-balanced chunker for C-family languages (TypeScript, JavaScript, Java, C, C++, Go,
/// Rust, Kotlin, Scala, Swift, PHP). Walks the source character-by-character tracking brace
/// depth while properly skipping characters inside string literals (single, double, backtick
/// with template-literal interpolation), line comments (//), and block comments (/* */).
/// Emits one chunk each time depth returns to 0 after being non-zero (i.e., a top-level
/// `{ ... }` block closes). Leading content before the first `{` becomes its own chunk.
///
/// Known limitation: this is not a real parser. Edge cases like regex literals
/// (e.g., <c>/}{/</c>) or JSX may slightly miscount, but they affect chunk boundaries only,
/// not correctness of any single chunk's content.
/// </summary>
public sealed class BraceBalancedChunker : IChunkStrategy
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "typescript", "javascript", "java", "cpp", "c",
        "go", "rust", "kotlin", "scala", "swift", "php",
    };

    public bool CanHandle(string? language) => language is not null && Supported.Contains(language);

    public IReadOnlyList<string> Chunk(string content, ChunkingOptions options)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [];

        var blocks = ExtractTopLevelBlocks(content);
        return blocks.Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b.Trim()).ToList();
    }

    private static List<string> ExtractTopLevelBlocks(string src)
    {
        var blocks = new List<string>();
        var depth = 0;
        var blockStart = 0;
        var state = ScanState.Code;
        // Stack of brace-depths captured when we entered each ${...} interpolation.
        // When depth returns to that captured value we know the interpolation is closing.
        var templateStack = new Stack<int>();

        for (var i = 0; i < src.Length; i++)
        {
            var c = src[i];
            switch (state)
            {
                case ScanState.LineComment:
                    if (c == '\n') state = ScanState.Code;
                    continue;

                case ScanState.BlockComment:
                    if (c == '*' && i + 1 < src.Length && src[i + 1] == '/') { state = ScanState.Code; i++; }
                    continue;

                case ScanState.SingleString:
                    if (c == '\\' && i + 1 < src.Length) { i++; continue; }
                    if (c == '\'') state = ScanState.Code;
                    continue;

                case ScanState.DoubleString:
                    if (c == '\\' && i + 1 < src.Length) { i++; continue; }
                    if (c == '"') state = ScanState.Code;
                    continue;

                case ScanState.Template:
                    if (c == '\\' && i + 1 < src.Length) { i++; continue; }
                    if (c == '`') { state = ScanState.Code; continue; }
                    if (c == '$' && i + 1 < src.Length && src[i + 1] == '{')
                    {
                        templateStack.Push(depth);
                        depth++;
                        state = ScanState.Code;
                        i++;
                    }
                    continue;

                case ScanState.Code:
                default:
                    if (c == '/' && i + 1 < src.Length && src[i + 1] == '/') { state = ScanState.LineComment; i++; continue; }
                    if (c == '/' && i + 1 < src.Length && src[i + 1] == '*') { state = ScanState.BlockComment; i++; continue; }
                    if (c == '\'') { state = ScanState.SingleString; continue; }
                    if (c == '"') { state = ScanState.DoubleString; continue; }
                    if (c == '`') { state = ScanState.Template; continue; }

                    if (c == '{') depth++;
                    else if (c == '}')
                    {
                        depth--;
                        // If we just closed a brace that matches a template-literal interpolation,
                        // pop and resume template scanning.
                        if (templateStack.Count > 0 && depth == templateStack.Peek())
                        {
                            templateStack.Pop();
                            state = ScanState.Template;
                            continue;
                        }

                        if (depth == 0)
                        {
                            blocks.Add(src.Substring(blockStart, i + 1 - blockStart));
                            blockStart = i + 1;
                        }
                    }
                    break;
            }
        }

        // Trailing content (anything after the last balanced top-level block).
        if (blockStart < src.Length)
            blocks.Add(src.Substring(blockStart));

        return blocks;
    }

    private enum ScanState { Code, LineComment, BlockComment, SingleString, DoubleString, Template }
}
