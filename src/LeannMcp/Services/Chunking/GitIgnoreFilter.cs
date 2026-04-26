namespace LeannMcp.Services.Chunking;

/// <summary>
/// Lightweight .gitignore pattern matcher.
/// Reads .gitignore files at each directory level and checks paths against accumulated patterns.
/// </summary>
public sealed class GitIgnoreFilter
{
    private readonly List<(string Pattern, bool IsNegation, bool IsDirectoryOnly)> _rules = [];

    /// <summary>
    /// Loads .gitignore rules from a file, anchored relative to the given base directory.
    /// </summary>
    public void LoadFromFile(string gitignorePath, string baseDir)
    {
        if (!File.Exists(gitignorePath)) return;
        var ignoreDir = Path.GetDirectoryName(Path.GetFullPath(gitignorePath));
        if (ignoreDir is null) return;

        var relativeBase = Path.GetRelativePath(baseDir, ignoreDir).Replace('\\', '/');
        if (relativeBase == ".") relativeBase = "";

        AddPatterns(File.ReadLines(gitignorePath), relativeBase);
    }

    /// <summary>
    /// Adds gitignore-style patterns from any source (e.g. CLI --exclude-paths).
    /// Patterns support **, *, ?, leading ! for negation, and trailing / for directory-only.
    /// </summary>
    public void AddPatterns(IEnumerable<string> patterns) => AddPatterns(patterns, basePath: "");

    private void AddPatterns(IEnumerable<string> patterns, string basePath)
    {
        foreach (var rawLine in patterns)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            var isNegation = line[0] == '!';
            if (isNegation) line = line[1..];

            var isDirectoryOnly = line.EndsWith('/');
            if (isDirectoryOnly) line = line.TrimEnd('/');

            line = ScopePattern(line, basePath);

            _rules.Add((line, isNegation, isDirectoryOnly));
        }
    }

    private static string ScopePattern(string pattern, string basePath)
    {
        var rooted = pattern.StartsWith('/');
        if (rooted) pattern = pattern.TrimStart('/');

        if (string.IsNullOrEmpty(basePath))
            return rooted || pattern.Contains('/') ? pattern : "**/" + pattern;

        if (rooted || pattern.Contains('/'))
            return basePath + "/" + pattern;

        return basePath + "/**/" + pattern;
    }

    /// <summary>
    /// Returns true if the given relative path should be ignored.
    /// </summary>
    public bool IsIgnored(string relativePath, bool isDirectory)
    {
        var ignored = false;
        var normalizedPath = relativePath.Replace('\\', '/');

        foreach (var (pattern, isNegation, isDirectoryOnly) in _rules)
        {
            if (isDirectoryOnly && !isDirectory) continue;

            if (MatchesGlob(normalizedPath, pattern))
                ignored = !isNegation;
        }

        return ignored;
    }

    /// <summary>
    /// Simple glob matcher supporting *, **, and ? wildcards.
    /// </summary>
    internal static bool MatchesGlob(string path, string pattern)
    {
        // Handle **/ prefix (match any directory depth)
        if (pattern.StartsWith("**/"))
        {
            var rest = pattern[3..];
            // Match at any depth
            return MatchesSimple(path, rest)
                || path.Split('/').Select((_, i) =>
                    string.Join('/', path.Split('/').Skip(i + 1)))
                    .Any(sub => sub.Length > 0 && MatchesSimple(sub, rest));
        }

        // Handle /** suffix (match everything inside)
        if (pattern.EndsWith("/**"))
        {
            var prefix = pattern[..^3];
            return path.StartsWith(prefix + "/", StringComparison.Ordinal)
                || path.Equals(prefix, StringComparison.Ordinal);
        }

        // Handle /**/ in the middle
        if (pattern.Contains("/**/"))
        {
            var parts = pattern.Split("/**/", 2);
            if (!MatchesSimple(path, parts[0] + "/**"))
                return false;
            // Check if rest matches any suffix
            var suffixPattern = "**/" + parts[1];
            return MatchesGlob(path, suffixPattern);
        }

        return MatchesSimple(path, pattern);
    }

    private static bool MatchesSimple(string text, string pattern)
    {
        return MatchSimpleRecursive(text, 0, pattern, 0);
    }

    private static bool MatchSimpleRecursive(string text, int ti, string pattern, int pi)
    {
        while (pi < pattern.Length && ti < text.Length)
        {
            if (pattern[pi] == '*')
            {
                // Skip consecutive *
                while (pi < pattern.Length && pattern[pi] == '*') pi++;
                if (pi == pattern.Length) return true;

                for (var i = ti; i <= text.Length; i++)
                {
                    if (MatchSimpleRecursive(text, i, pattern, pi))
                        return true;
                }
                return false;
            }

            if (pattern[pi] == '?' || pattern[pi] == text[ti])
            {
                pi++;
                ti++;
            }
            else
            {
                return false;
            }
        }

        // Skip trailing wildcards
        while (pi < pattern.Length && pattern[pi] == '*') pi++;

        return ti == text.Length && pi == pattern.Length;
    }
}
