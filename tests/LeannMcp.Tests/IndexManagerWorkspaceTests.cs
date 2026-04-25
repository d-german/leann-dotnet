using System.Collections.Immutable;
using CSharpFunctionalExtensions;
using LeannMcp.Models;
using LeannMcp.Services;
using LeannMcp.Services.Workspace;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeannMcp.Tests;

[Collection("WorkspaceResolverTests")]
public class IndexManagerWorkspaceTests : IDisposable
{
    private const string EnvVar = "LEANN_DATA_ROOT";
    private readonly string? _originalEnv;

    public IndexManagerWorkspaceTests()
    {
        _originalEnv = Environment.GetEnvironmentVariable(EnvVar);
        Environment.SetEnvironmentVariable(EnvVar, null);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(EnvVar, _originalEnv);

    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public Result<float[]> ComputeEmbedding(string text) => new float[768];
        public Result<float[][]> ComputeEmbeddings(IReadOnlyList<string> texts)
            => texts.Select(_ => new float[768]).ToArray();
        public void Warmup() { }
    }

    [Fact]
    public void IndexesDir_TracksResolverAcrossCalls()
    {
        var store = new WorkspaceRootStore();
        var resolver = new WorkspaceResolver(store, NullLogger<WorkspaceResolver>.Instance);
        var descriptor = ModelRegistry.GetById(ModelRegistry.JinaCodeId).Value;
        var manager = new IndexManager(new StubEmbeddingService(), descriptor, NullLogger<IndexManager>.Instance, resolver);

        store.SetListRoots(ImmutableArray.Create("C:\\workspace-a"));
        var first = manager.IndexesDir;

        store.SetListRoots(ImmutableArray.Create("C:\\workspace-b"));
        var second = manager.IndexesDir;

        Assert.Equal(Path.Combine("C:\\workspace-a", ".leann", "indexes"), first);
        Assert.Equal(Path.Combine("C:\\workspace-b", ".leann", "indexes"), second);
    }

    [Fact]
    public void DiscoverIndexNames_UsesCurrentResolverPath()
    {
        var store = new WorkspaceRootStore();
        var resolver = new WorkspaceResolver(store, NullLogger<WorkspaceResolver>.Instance);
        var descriptor = ModelRegistry.GetById(ModelRegistry.JinaCodeId).Value;
        var manager = new IndexManager(new StubEmbeddingService(), descriptor, NullLogger<IndexManager>.Instance, resolver);

        var temp = Path.Combine(Path.GetTempPath(), "leann-mgr-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        var indexDir = Path.Combine(temp, ".leann", "indexes", "myidx");
        Directory.CreateDirectory(indexDir);
        File.WriteAllText(Path.Combine(indexDir, "documents.leann.meta.json"), "{}");
        try
        {
            store.SetListRoots(ImmutableArray.Create(temp));
            var names = manager.DiscoverIndexNames();
            Assert.Contains("myidx", names);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void StaticIndexesDir_StillWorks_ForCliMode()
    {
        var descriptor = ModelRegistry.GetById(ModelRegistry.JinaCodeId).Value;
        var manager = new IndexManager(
            new StubEmbeddingService(),
            descriptor,
            NullLogger<IndexManager>.Instance,
            indexesDir: "C:\\static\\indexes");
        Assert.Equal("C:\\static\\indexes", manager.IndexesDir);
    }
}
