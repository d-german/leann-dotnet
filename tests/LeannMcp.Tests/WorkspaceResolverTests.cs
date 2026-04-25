using System.Collections.Immutable;
using LeannMcp.Services.Workspace;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeannMcp.Tests;

[Collection("WorkspaceResolverTests")]
public class WorkspaceResolverTests : IDisposable
{
    private const string EnvVar = "LEANN_DATA_ROOT";
    private readonly string? _originalEnv;

    public WorkspaceResolverTests()
    {
        _originalEnv = Environment.GetEnvironmentVariable(EnvVar);
        Environment.SetEnvironmentVariable(EnvVar, null);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(EnvVar, _originalEnv);

    private static WorkspaceResolver MakeResolver(out WorkspaceRootStore store)
    {
        store = new WorkspaceRootStore();
        return new WorkspaceResolver(store, NullLogger<WorkspaceResolver>.Instance);
    }

    [Fact]
    public void EnvVar_WinsOverEverything()
    {
        var resolver = MakeResolver(out var store);
        store.SetListRoots(ImmutableArray.Create("C:\\from-roots"));
        Environment.SetEnvironmentVariable(EnvVar, "C:\\from-env");

        Assert.Equal("C:\\from-env", resolver.ResolveDataRoot());
    }

    [Fact]
    public void Roots_UsedWhenEnvEmpty()
    {
        var resolver = MakeResolver(out var store);
        store.SetListRoots(ImmutableArray.Create("C:\\from-roots"));

        Assert.Equal("C:\\from-roots", resolver.ResolveDataRoot());
    }

    [Fact]
    public void Cwd_UsedWhenEnvAndRootsEmpty()
    {
        var resolver = MakeResolver(out _);
        Assert.Equal(Directory.GetCurrentDirectory(), resolver.ResolveDataRoot());
    }

    [Fact]
    public void ListRoots_PreferredOverInitializeRoots()
    {
        var resolver = MakeResolver(out var store);
        store.SetInitializeRoots(ImmutableArray.Create("C:\\init"));
        store.SetListRoots(ImmutableArray.Create("C:\\list"));

        Assert.Equal("C:\\list", resolver.ResolveDataRoot());
    }

    [Fact]
    public void InitializeRoots_FallbackWhenListRootsEmpty()
    {
        var resolver = MakeResolver(out var store);
        store.SetInitializeRoots(ImmutableArray.Create("C:\\init"));

        Assert.Equal("C:\\init", resolver.ResolveDataRoot());
    }

    [Fact]
    public void ResolveIndexesDir_AppendsLeannIndexes()
    {
        var resolver = MakeResolver(out _);
        Environment.SetEnvironmentVariable(EnvVar, "C:\\workspace");

        var expected = Path.Combine("C:\\workspace", ".leann", "indexes");
        Assert.Equal(expected, resolver.ResolveIndexesDir());
    }

    [Fact]
    public void RootContainingDotLeann_PreferredOverFirst()
    {
        var resolver = MakeResolver(out var store);
        var temp = Path.Combine(Path.GetTempPath(), "leann-resolver-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        var withLeann = Path.Combine(temp, "with");
        var without = Path.Combine(temp, "without");
        Directory.CreateDirectory(Path.Combine(withLeann, ".leann"));
        Directory.CreateDirectory(without);
        try
        {
            store.SetListRoots(ImmutableArray.Create(without, withLeann));
            Assert.Equal(withLeann, resolver.ResolveDataRoot());
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void InvalidateRootsCache_ClearsStore()
    {
        var resolver = MakeResolver(out var store);
        store.SetListRoots(ImmutableArray.Create("C:\\x"));
        resolver.InvalidateRootsCache();
        Assert.True(store.Snapshot.ListRoots.IsEmpty);
    }

    [Fact]
    public async Task EnsureResolvedAsync_NullServer_NoOp()
    {
        var resolver = MakeResolver(out var store);
        await resolver.EnsureResolvedAsync(null, CancellationToken.None);
        Assert.True(store.Snapshot.ListRoots.IsEmpty);
    }

    [Fact]
    public async Task EnsureResolvedAsync_EnvSet_SkipsFetch()
    {
        var resolver = MakeResolver(out var store);
        Environment.SetEnvironmentVariable(EnvVar, "C:\\set");
        await resolver.EnsureResolvedAsync(null, CancellationToken.None);
        Assert.True(store.Snapshot.ListRoots.IsEmpty);
    }
}
