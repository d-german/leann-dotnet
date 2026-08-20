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

        LoadIgnoreFilesRecursive(filter, rootFull, rootFull);
        LoadGlobalIgnoreFile(filter, options.GlobalIgnoreFile);

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
        try
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
                AbsolutePath = Path.GetFullPath(filePath),
                CreationDate = fileInfo.CreationTimeUtc,
                LastModifiedDate = fileInfo.LastWriteTimeUtc,
                IsCode = language is not null,
                Language = language,
                SourceType = sourceType,
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Skipping {Path}: document reader threw unexpectedly", filePath);
            return null;
        }
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

    /// <summary>Ignore file names honoured at every directory level, in load order.</summary>
    private static readonly string[] IgnoreFileNames = [".gitignore", ".leannignore"];

    private static void LoadIgnoreFilesRecursive(GitIgnoreFilter filter, string dir, string rootDir)
    {
        foreach (var ignoreFileName in IgnoreFileNames)
            filter.LoadFromFile(Path.Combine(dir, ignoreFileName), rootDir);

        foreach (var subDir in EnumerateDirectoriesSafe(dir))
        {
            var dirName = Path.GetFileName(subDir);
            if (dirName.StartsWith('.')) continue;

            LoadIgnoreFilesRecursive(filter, subDir, rootDir);
        }
    }

    /// <summary>
    /// Applies a workspace-wide ignore file that lives outside the repository being
    /// indexed. Its patterns are added unanchored, so they read as repo-relative
    /// rather than being scoped to the file's own directory.
    /// </summary>
    private void LoadGlobalIgnoreFile(GitIgnoreFilter filter, string? globalIgnoreFile)
    {
        if (string.IsNullOrWhiteSpace(globalIgnoreFile) || !File.Exists(globalIgnoreFile))
            return;

        filter.AddPatterns(File.ReadLines(globalIgnoreFile));
        logger.LogInformation("Applying workspace ignore file {Path}", globalIgnoreFile);
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

    private static string[] EnumerateFilesSafe(string dir)
    {
        // Directory.EnumerateFiles is lazy, so returning it from inside the
        // try block lets access errors escape later during foreach.
        try { return Directory.GetFiles(dir); }
        catch { return []; }
    }

    private static string[] EnumerateDirectoriesSafe(string dir)
    {
        try { return Directory.GetDirectories(dir); }
        catch { return []; }
    }
}
