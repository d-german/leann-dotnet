using System.Collections.Immutable;

namespace LeannMcp.Services.Workspace;

/// <summary>
/// Immutable snapshot of MCP-client-advertised roots.
/// Decouples protocol delivery from resolution.
/// </summary>
public sealed record WorkspaceRootSnapshot
{
    public required ImmutableArray<string> ListRoots { get; init; }
    public required ImmutableArray<string> InitializeRoots { get; init; }

    public static WorkspaceRootSnapshot Empty { get; } = new()
    {
        ListRoots = ImmutableArray<string>.Empty,
        InitializeRoots = ImmutableArray<string>.Empty
    };
}

/// <summary>
/// Holds the latest <see cref="WorkspaceRootSnapshot"/>. Lock-free reads via
/// volatile reference swap; mutation is single-writer (resolver semaphore).
/// </summary>
public sealed class WorkspaceRootStore
{
    private WorkspaceRootSnapshot _snapshot = WorkspaceRootSnapshot.Empty;

    public WorkspaceRootSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public void SetListRoots(ImmutableArray<string> listRoots)
    {
        var current = Volatile.Read(ref _snapshot);
        Volatile.Write(ref _snapshot, current with { ListRoots = listRoots });
    }

    public void SetInitializeRoots(ImmutableArray<string> initializeRoots)
    {
        var current = Volatile.Read(ref _snapshot);
        Volatile.Write(ref _snapshot, current with { InitializeRoots = initializeRoots });
    }

    public void Clear() => Volatile.Write(ref _snapshot, WorkspaceRootSnapshot.Empty);
}

/// <summary>
/// Pure conversion helpers from MCP <c>file:///</c> URIs to local OS paths.
/// </summary>
public static class WorkspaceRootConverter
{
    public static ImmutableArray<string> FromUriStrings(IEnumerable<string?> uris)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var raw in uris)
        {
            if (TryGetFilePath(raw, out var path))
                builder.Add(path);
        }
        return builder.ToImmutable();
    }

    private static bool TryGetFilePath(string? raw, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri) || !uri.IsFile)
            return false;

        var local = uri.LocalPath;
        if (string.IsNullOrWhiteSpace(local)) return false;

        path = Path.GetFullPath(local);
        return true;
    }
}
