using CSharpFunctionalExtensions;
using LeannMcp.Models;
using Microsoft.Extensions.Logging;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Discovers files in a directory tree, respecting .gitignore rules and extension filters.
/// </summary>
public sealed class FileDiscoveryService(ILogger<FileDiscoveryService> logger) : IFileDiscovery
{
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bin", ".obj", ".o", ".so", ".dylib",
        ".zip", ".gz", ".tar", ".7z", ".rar",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg", ".webp",
        ".mp3", ".mp4", ".avi", ".mov", ".wav",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".pptx",
        ".woff", ".woff2", ".ttf", ".eot",
        ".pyc", ".pyo", ".class",
        ".lock", ".pickle",
    };

    public Result<IReadOnlyList<SourceDocument>> DiscoverFiles(string rootPath, ChunkingOptions options)
    {
        if (!Directory.Exists(rootPath))
            return Result.Failure<IReadOnlyList<SourceDocument>>($"Directory not found: {rootPath}");

        var rootFull = Path.GetFullPath(rootPath);
        var filter = new GitIgnoreFilter();
        var supportedExtensions = options.IncludeExtensions ?? FileExtensions.AllSupported;
        var documents = new List<SourceDocument>();

        LoadGitIgnoreRecursive(filter, rootFull);

        logger.LogInformation("Discovering files in {Root}", rootFull);
        WalkDirectory(rootFull, rootFull, filter, supportedExtensions, options.IncludeHidden, documents);
        logger.LogInformation("Discovered {Count} files in {Root}", documents.Count, rootFull);

        return Result.Success<IReadOnlyList<SourceDocument>>(documents);
    }

    private void WalkDirectory(
        string dir,
        string rootDir,
        GitIgnoreFilter filter,
        IReadOnlySet<string> supportedExtensions,
        bool includeHidden,
        List<SourceDocument> results)
    {
        var relativeDirPath = GetRelativePath(dir, rootDir);

        // Skip hidden directories
        if (!includeHidden && IsHiddenPath(dir) && dir != rootDir)
            return;

        // Check if directory is gitignored
        if (relativeDirPath.Length > 0 && filter.IsIgnored(relativeDirPath, isDirectory: true))
            return;

        // Process files in this directory
        foreach (var filePath in EnumerateFilesSafe(dir))
        {
            var fileName = Path.GetFileName(filePath);
            if (!includeHidden && fileName.StartsWith('.'))
                continue;

            var ext = Path.GetExtension(filePath);
            if (BinaryExtensions.Contains(ext))
                continue;

            if (!supportedExtensions.Contains(ext))
                continue;

            var relativeFilePath = GetRelativePath(filePath, rootDir);
            if (filter.IsIgnored(relativeFilePath, isDirectory: false))
                continue;

            var doc = TryLoadDocument(filePath, relativeFilePath);
            if (doc is not null)
            {
                results.Add(doc);
                if (results.Count % 100 == 0)
                    logger.LogInformation("  {Count} files discovered...", results.Count);
            }
        }

        // Recurse into subdirectories
        foreach (var subDir in EnumerateDirectoriesSafe(dir))
        {
            WalkDirectory(subDir, rootDir, filter, supportedExtensions, includeHidden, results);
        }
    }

    private static SourceDocument? TryLoadDocument(string filePath, string relativePath)
    {
        try
        {
            var content = File.ReadAllText(filePath);
            var fileInfo = new FileInfo(filePath);
            var ext = Path.GetExtension(filePath);
            var language = FileExtensions.GetLanguage(ext);

            return new SourceDocument
            {
                Content = content,
                FilePath = relativePath,
                FileName = Path.GetFileName(filePath),
                CreationDate = fileInfo.CreationTimeUtc,
                LastModifiedDate = fileInfo.LastWriteTimeUtc,
                IsCode = language is not null,
                Language = language,
            };
        }
        catch
        {
            return null;
        }
    }

    private static void LoadGitIgnoreRecursive(GitIgnoreFilter filter, string rootDir)
    {
        var gitignorePath = Path.Combine(rootDir, ".gitignore");
        filter.LoadFromFile(gitignorePath, rootDir);

        foreach (var subDir in EnumerateDirectoriesSafe(rootDir))
        {
            var dirName = Path.GetFileName(subDir);
            if (dirName.StartsWith('.')) continue; // Skip .git etc.

            var subGitignore = Path.Combine(subDir, ".gitignore");
            if (File.Exists(subGitignore))
                filter.LoadFromFile(subGitignore, rootDir);
        }
    }

    private static string GetRelativePath(string fullPath, string rootDir)
    {
        var relative = Path.GetRelativePath(rootDir, fullPath);
        return relative.Replace('\\', '/');
    }

    private static bool IsHiddenPath(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith('.') && name != "." && name != "..";
    }

    private static IEnumerable<string> EnumerateFilesSafe(string dir)
    {
        try { return Directory.EnumerateFiles(dir); }
        catch { return []; }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string dir)
    {
        try { return Directory.EnumerateDirectories(dir); }
        catch { return []; }
    }
}
