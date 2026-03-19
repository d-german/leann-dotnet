namespace LeannMcp.Models;

/// <summary>
/// Represents a loaded source file ready for chunking.
/// </summary>
public sealed record SourceDocument
{
    public required string Content { get; init; }
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public DateTime? CreationDate { get; init; }
    public DateTime? LastModifiedDate { get; init; }
    public bool IsCode { get; init; }
    public string? Language { get; init; }
}

/// <summary>
/// Maps file extensions to language names and categorizes supported file types.
/// </summary>
public static class FileExtensions
{
    /// <summary>
    /// Code file extensions mapped to their language identifiers.
    /// Matches Python's CODE_EXTENSIONS plus additional languages.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> CodeLanguageMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Languages from Python CODE_EXTENSIONS
            [".py"] = "python",
            [".java"] = "java",
            [".cs"] = "csharp",
            [".ts"] = "typescript",
            [".tsx"] = "typescript",
            [".js"] = "javascript",
            [".jsx"] = "javascript",
            // Additional languages
            [".cpp"] = "cpp",
            [".c"] = "c",
            [".h"] = "c",
            [".hpp"] = "cpp",
            [".go"] = "go",
            [".rs"] = "rust",
            [".rb"] = "ruby",
            [".php"] = "php",
            [".swift"] = "swift",
            [".kt"] = "kotlin",
            [".scala"] = "scala",
            [".r"] = "r",
            [".R"] = "r",
            [".jl"] = "julia",
            [".sql"] = "sql",
            [".sh"] = "bash",
            [".bash"] = "bash",
            [".zsh"] = "zsh",
            [".fish"] = "fish",
            [".ps1"] = "powershell",
            [".bat"] = "batch",
        };

    /// <summary>
    /// Text/config file extensions that are not code but should be indexed.
    /// </summary>
    public static readonly IReadOnlySet<string> TextExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".markdown", ".rst",
            ".json", ".yaml", ".yml", ".xml", ".toml", ".ini", ".cfg", ".conf",
            ".html", ".htm", ".css", ".scss", ".less",
            ".vue", ".svelte",
            ".dockerfile", ".dockerignore",
            ".editorconfig", ".gitattributes",
        };

    /// <summary>
    /// All supported file extensions (code + text).
    /// </summary>
    public static readonly IReadOnlySet<string> AllSupported =
        new HashSet<string>(
            CodeLanguageMap.Keys.Concat(TextExtensions),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the language name for a file extension, or null if not a code file.
    /// </summary>
    public static string? GetLanguage(string extension) =>
        CodeLanguageMap.TryGetValue(extension, out var lang) ? lang : null;

    /// <summary>
    /// Returns true if the extension is recognized as a code file.
    /// </summary>
    public static bool IsCodeExtension(string extension) =>
        CodeLanguageMap.ContainsKey(extension);

    /// <summary>
    /// Returns true if the extension is supported (code or text).
    /// </summary>
    public static bool IsSupported(string extension) =>
        AllSupported.Contains(extension);
}
