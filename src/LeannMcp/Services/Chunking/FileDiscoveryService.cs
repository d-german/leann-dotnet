using CSharpFunctionalExtensions;
using LeannMcp.Models;
using Microsoft.Extensions.Logging;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Discovers files in a directory tree, respecting .gitignore rules and extension filters.
/// </summary>
public sealed class FileDiscoveryService(
    ILogger<FileDiscoveryService> logger,
    IEnumerable<IDocumentReader> documentReaders) : IFileDiscovery
{
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bin", ".obj", ".o", ".so", ".dylib",
        ".zip", ".gz", ".tar", ".7z", ".rar",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg", ".webp",
        ".mp3", ".mp4", ".avi", ".mov", ".wav",
        ".doc", ".docx", ".xls", ".xlsx", ".pptx",
        ".woff", ".woff2", ".ttf", ".eot",
        ".pyc", ".pyo", ".class",
        ".lock", ".pickle",
    };

    private readonly IReadOnlyList<IDocumentReader> _readers = documentReaders.ToList();

    public Result<IReadOnlyList<SourceDocument>> DiscoverFiles(string rootPath, ChunkingOptions options)
    {
        if (!Directory.Exists(rootPath))
            return Result.Failure<IReadOnlyList<SourceDocument>>($"Directory not found: {rootPath}");

        var rootFull = Path.GetFullPath(rootPath);
        var filter = new GitIgnoreFilter();
        var supportedExtensions = options.IncludeExtensions ?? FileExtensions.AllSupported;
        var documents = new List<SourceDocument>();

        LoadGitIgnoreRecursive(filter, rootFull);

        if (options.ExcludePaths is { Count: > 0 } extra)
        {
            filter.AddPatterns(extra);
            logger.LogInformation("Applying {Count} CLI exclude pattern(s)", extra.Count);
        }

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

        if (!includeHidden && IsHiddenPath(dir) && dir != rootDir)
            return;

        if (relativeDirPath.Length > 0 && filter.IsIgnored(relativeDirPath, isDirectory: true))
            return;

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

        foreach (var subDir in EnumerateDirectoriesSafe(dir))
        {
            WalkDirectory(subDir, rootDir, filter, supportedExtensions, includeHidden, results);
        }
    }

    private SourceDocument? TryLoadDocument(string filePath, string relativePath)
    {
        var ext = Path.GetExtension(filePath);
        var reader = SelectReader(ext);
        var readResult = reader.Read(filePath);
        if (readResult.IsFailure)
        {
            logger.LogWarning("Skipping {Path}: {Error}", filePath, readResult.Error);
            return null;
        }

        var fileInfo = new FileInfo(filePath);
        var language = FileExtensions.GetLanguage(ext);
        var sourceType = reader is PdfDocumentReader ? "pdf" : "text";

        return new SourceDocument
        {
            Content = readResult.Value,
            FilePath = relativePath,
            FileName = Path.GetFileName(filePath),
            CreationDate = fileInfo.CreationTimeUtc,
            LastModifiedDate = fileInfo.LastWriteTimeUtc,
            IsCode = language is not null,
            Language = language,
            SourceType = sourceType,
        };
    }

    private IDocumentReader SelectReader(string extension)
    {
        for (var i = 0; i < _readers.Count; i++)
        {
            var reader = _readers[i];
            if (reader is PlainTextReader)
                continue;
            if (reader.CanHandle(extension))
                return reader;
        }
        return _readers.OfType<PlainTextReader>().First();
    }

    private static void LoadGitIgnoreRecursive(GitIgnoreFilter filter, string rootDir)
    {
        var gitignorePath = Path.Combine(rootDir, ".gitignore");
        filter.LoadFromFile(gitignorePath, rootDir);

        foreach (var subDir in EnumerateDirectoriesSafe(rootDir))
        {
            var dirName = Path.GetFileName(subDir);
            if (dirName.StartsWith('.')) continue;

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
