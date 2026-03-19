using CSharpFunctionalExtensions;
using LeannMcp.Models;
using LeannMcp.Services.Chunking;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LeannMcp.Services.Watching;

/// <summary>
/// Background service that periodically checks git repos for new commits
/// and auto-rebuilds passage indexes when changes are detected.
/// </summary>
public sealed class RepoWatcherService(
    IFileDiscovery fileDiscovery,
    IDocumentChunker documentChunker,
    IPassageWriter passageWriter,
    IndexBuilder indexBuilder,
    ILogger<RepoWatcherService> logger,
    string configPath,
    int intervalSeconds,
    string indexesDir) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configResult = RepoConfigLoader.Load(configPath);
        if (configResult.IsFailure)
        {
            logger.LogError("Failed to load repo config: {Error}", configResult.Error);
            return;
        }

        var config = configResult.Value;
        var enabledRepos = config.Repos.Where(r => r.Enabled).ToList();
        var interval = TimeSpan.FromSeconds(intervalSeconds > 0 ? intervalSeconds : config.IntervalSeconds);

        logger.LogInformation(
            "LEANN Repo Watcher started — monitoring {Count} repos every {Interval}s",
            enabledRepos.Count, interval.TotalSeconds);

        // Initial scan after a short delay
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await ScanAllRepos(enabledRepos, stoppingToken);

            logger.LogInformation("Next scan in {Interval}s (Ctrl+C to stop)", interval.TotalSeconds);
            await Task.Delay(interval, stoppingToken);
        }

        logger.LogInformation("Repo watcher stopped");
    }

    private async Task ScanAllRepos(IReadOnlyList<RepoEntry> repos, CancellationToken ct)
    {
        logger.LogInformation("--- Scanning {Count} repos for changes ---", repos.Count);

        int changed = 0, skipped = 0, errored = 0;

        foreach (var repo in repos)
        {
            if (ct.IsCancellationRequested) break;

            var result = await CheckAndRebuildRepo(repo, ct);
            if (result.IsFailure)
            {
                logger.LogWarning("[{Index}] Skipped: {Error}", repo.IndexName, result.Error);
                errored++;
            }
            else if (result.Value)
            {
                changed++;
            }
            else
            {
                skipped++;
            }
        }

        logger.LogInformation(
            "Scan complete — {Changed} rebuilt, {Skipped} unchanged, {Errored} errors",
            changed, skipped, errored);
    }

    /// <returns>true if rebuilt, false if unchanged</returns>
    private async Task<Result<bool>> CheckAndRebuildRepo(RepoEntry repo, CancellationToken ct)
    {
        if (!Directory.Exists(repo.Folder))
            return Result.Failure<bool>($"Folder not found: {repo.Folder}");

        var hasGit = GitService.HasGitDirectory(repo.Folder);
        if (hasGit.IsFailure || !hasGit.Value)
            return Result.Failure<bool>($"Not a git repo: {repo.Folder}");

        // Fetch latest from remote
        var fetchResult = await GitService.FetchAsync(repo.Folder, repo.Branch);
        if (fetchResult.IsFailure)
            return Result.Failure<bool>($"Fetch failed: {fetchResult.Error}");

        // Compare local HEAD vs remote
        var localHash = GitService.GetHeadHash(repo.Folder);
        var remoteHash = GitService.GetRemoteHash(repo.Folder, repo.Branch);

        if (localHash.IsFailure)
            return Result.Failure<bool>($"Cannot read HEAD: {localHash.Error}");
        if (remoteHash.IsFailure)
            return Result.Failure<bool>($"Cannot read origin/{repo.Branch}: {remoteHash.Error}");

        // Also check stored hash (last successful build)
        var storedHash = ReadStoredHash(repo.IndexName);

        if (localHash.Value == remoteHash.Value && localHash.Value == storedHash)
        {
            logger.LogDebug("[{Index}] No changes (HEAD={Hash})", repo.IndexName, localHash.Value[..8]);
            return Result.Success(false);
        }

        var oldHash = localHash.Value[..Math.Min(8, localHash.Value.Length)];

        // Pull changes if remote is ahead
        if (localHash.Value != remoteHash.Value)
        {
            logger.LogInformation("[{Index}] Changes detected, pulling {Branch}...", repo.IndexName, repo.Branch);
            var pullResult = await GitService.PullAsync(repo.Folder, repo.Branch);
            if (pullResult.IsFailure)
            {
                logger.LogWarning("[{Index}] Pull failed: {Error}. Attempting reset...", repo.IndexName, pullResult.Error);
                // Try hard reset as fallback
                var resetResult = await GitService.FetchAsync(repo.Folder, repo.Branch);
                if (resetResult.IsFailure)
                    return Result.Failure<bool>($"Pull and fetch both failed for {repo.IndexName}");
            }
        }
        else
        {
            logger.LogInformation("[{Index}] Stored hash differs from HEAD, rebuilding...", repo.IndexName);
        }

        if (ct.IsCancellationRequested) return Result.Success(false);

        // Rebuild: chunk + embed
        var rebuildResult = RebuildIndex(repo);
        if (rebuildResult.IsFailure)
            return Result.Failure<bool>($"Rebuild failed: {rebuildResult.Error}");

        // Store new hash
        var newHash = GitService.GetHeadHash(repo.Folder);
        if (newHash.IsSuccess)
        {
            WriteStoredHash(repo.IndexName, newHash.Value);
            var newShort = newHash.Value[..Math.Min(8, newHash.Value.Length)];
            logger.LogInformation("[{Index}] Updated ({Old} -> {New})", repo.IndexName, oldHash, newShort);
        }

        return Result.Success(true);
    }

    private Result RebuildIndex(RepoEntry repo)
    {
        var options = new ChunkingOptions();
        var indexDir = Path.Combine(indexesDir, repo.IndexName);

        // Step 1: Discover files
        var discoverResult = fileDiscovery.DiscoverFiles(repo.Folder, options);
        if (discoverResult.IsFailure)
            return Result.Failure($"Discovery failed: {discoverResult.Error}");

        var documents = discoverResult.Value;
        logger.LogInformation("[{Index}] Discovered {Count} files", repo.IndexName, documents.Count);

        // Step 2: Chunk
        var chunkResult = documentChunker.ChunkDocuments(documents, options);
        if (chunkResult.IsFailure)
            return Result.Failure($"Chunking failed: {chunkResult.Error}");

        var passages = chunkResult.Value;
        logger.LogInformation("[{Index}] Created {Count} passages", repo.IndexName, passages.Count);

        // Step 3: Write passages
        var syncRoots = new List<string> { Path.GetFullPath(repo.Folder) };
        var writeResult = passageWriter.WritePassages(indexDir, repo.IndexName, passages, syncRoots);
        if (writeResult.IsFailure)
            return Result.Failure($"Write failed: {writeResult.Error}");

        // Step 4: Build embeddings
        var embedResult = indexBuilder.BuildIndex(indexDir, repo.IndexName, batchSize: 32, force: true);
        if (embedResult.IsFailure)
            return Result.Failure($"Embedding failed: {embedResult.Error}");

        return Result.Success();
    }

    private string GetHashFilePath(string indexName) =>
        Path.Combine(indexesDir, indexName, ".git-hash");

    private string? ReadStoredHash(string indexName)
    {
        var path = GetHashFilePath(indexName);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    private void WriteStoredHash(string indexName, string hash)
    {
        var dir = Path.GetDirectoryName(GetHashFilePath(indexName));
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllText(GetHashFilePath(indexName), hash);
    }
}