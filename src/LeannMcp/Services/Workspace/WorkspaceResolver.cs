using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LeannMcp.Services.Workspace;

/// <summary>
/// Resolves the data root and indexes directory using the priority:
/// <c>LEANN_DATA_ROOT</c> env > MCP client roots > <c>Directory.GetCurrentDirectory()</c>.
/// </summary>
public sealed class WorkspaceResolver
{
    private const string DataRootEnvVar = "LEANN_DATA_ROOT";
    private const string LeannSubdir = ".leann";
    private const string IndexesSubdir = "indexes";

    private readonly WorkspaceRootStore _store;
    private readonly ILogger<WorkspaceResolver> _logger;
    private readonly SemaphoreSlim _fetchGate = new(1, 1);

    public WorkspaceResolver(WorkspaceRootStore store, ILogger<WorkspaceResolver> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Returns the absolute data root using current state (no I/O).
    /// </summary>
    public string ResolveDataRoot()
    {
        var env = Environment.GetEnvironmentVariable(DataRootEnvVar);
        if (!string.IsNullOrWhiteSpace(env)) return env;

        var preferred = PickPreferredRoot(_store.Snapshot);
        if (preferred is not null) return preferred;

        return Directory.GetCurrentDirectory();
    }

    public string ResolveIndexesDir()
        => Path.Combine(ResolveDataRoot(), LeannSubdir, IndexesSubdir);

    /// <summary>
    /// On first call (and whenever the store is empty), invokes
    /// <see cref="McpServer.RequestRootsAsync"/> to populate the store.
    /// Idempotent &amp; concurrency-safe; failures are logged and swallowed.
    /// </summary>
    public async ValueTask EnsureResolvedAsync(McpServer? server, CancellationToken ct)
    {
        if (server is null) return;
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DataRootEnvVar))) return;
        if (HasRoots(_store.Snapshot)) return;

        await _fetchGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (HasRoots(_store.Snapshot)) return;
            await FetchAndStoreRootsAsync(server, ct).ConfigureAwait(false);
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    /// <summary>Drops cached roots so the next <see cref="EnsureResolvedAsync"/> re-fetches.</summary>
    public void InvalidateRootsCache() => _store.Clear();

    private static bool HasRoots(WorkspaceRootSnapshot snapshot)
        => !snapshot.ListRoots.IsDefaultOrEmpty && snapshot.ListRoots.Length > 0;

    private async Task FetchAndStoreRootsAsync(McpServer server, CancellationToken ct)
    {
        try
        {
            var result = await server.RequestRootsAsync(new ListRootsRequestParams(), ct).ConfigureAwait(false);
            var uris = (result?.Roots ?? new List<Root>()).Select(r => r.Uri);
            var paths = WorkspaceRootConverter.FromUriStrings(uris);
            _store.SetListRoots(paths);
            _logger.LogInformation("Resolved {Count} workspace roots from MCP client.", paths.Length);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Client does not advertise roots; falling back to cwd.");
        }
    }

    private static string? PickPreferredRoot(WorkspaceRootSnapshot snapshot)
    {
        var rootsToConsider = snapshot.ListRoots.IsDefaultOrEmpty || snapshot.ListRoots.Length == 0
            ? snapshot.InitializeRoots
            : snapshot.ListRoots;

        if (rootsToConsider.IsDefaultOrEmpty) return null;

        var withLeann = rootsToConsider.FirstOrDefault(IsLeannProject);
        if (withLeann is not null) return withLeann;

        return rootsToConsider.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r));
    }

    private static bool IsLeannProject(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return false;
        try { return Directory.Exists(Path.Combine(root, LeannSubdir)); }
        catch { return false; }
    }
}
