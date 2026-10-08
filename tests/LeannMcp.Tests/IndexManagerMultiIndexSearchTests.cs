using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CSharpFunctionalExtensions;
using LeannMcp.Models;
using LeannMcp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeannMcp.Tests;

/// <summary>
/// Searching several indexes at once: selector resolution, one ranking across
/// indexes, identifier pins from any index, and results that name their index.
/// Embeddings are axis-aligned unit vectors so cosine order is fully controlled.
/// </summary>
public sealed class IndexManagerMultiIndexSearchTests : IDisposable
{
    private readonly string _root;
    private readonly string _indexesDir;
    private readonly EmbeddingModelDescriptor _model = ModelRegistry.GetById(ModelRegistry.ContrieverId).Value;

    public IndexManagerMultiIndexSearchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "leann-multi-" + Guid.NewGuid().ToString("N")[..8]);
        _indexesDir = Path.Combine(_root, ".leann", "indexes");
        Directory.CreateDirectory(_indexesDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>Embeds a query as the unit vector of its axis; unknown text embeds on axis 0.</summary>
    private sealed class AxisStubService(int dim, IReadOnlyDictionary<string, int> axisByQuery) : IEmbeddingService
    {
        public Result<float[]> ComputeEmbedding(string text) =>
            Axis(dim, axisByQuery.TryGetValue(text, out var axis) ? axis : 0);

        public Result<float[][]> ComputeEmbeddings(IReadOnlyList<string> texts) =>
            texts.Select(t => ComputeEmbedding(t).Value).ToArray();

        public void Warmup() { }
    }

    private sealed class StaticFactory(IEmbeddingService service) : IEmbeddingServiceFactory
    {
        public Result<IEmbeddingService> GetOrCreate(EmbeddingModelDescriptor descriptor) => Result.Success(service);

        public IReadOnlyCollection<string> LoadedModelIds => Array.Empty<string>();
    }

    private static float[] Axis(int dim, int axis)
    {
        var v = new float[dim];
        v[axis] = 1.0f;
        return v;
    }

    /// <summary>Writes an index whose passages embed on the given axes.</summary>
    private void WriteIndex(string indexName, params (string Id, string Text, int Axis)[] passages) =>
        WriteIndex(indexName, _model, passages);

    private void WriteIndex(string indexName, EmbeddingModelDescriptor model, params (string Id, string Text, int Axis)[] passages)
    {
        var indexDir = Path.Combine(_indexesDir, indexName);
        Directory.CreateDirectory(indexDir);

        var meta = new IndexMetadata(
            Version: "1.0",
            BackendName: "flat",
            EmbeddingModel: model.Id,
            Dimensions: model.Dimensions,
            EmbeddingMode: null,
            PassageSources:
            [
                new(Type: "jsonl", Path: null, IndexPath: null, PathRelative: "documents.leann.passages.jsonl", IndexPathRelative: null),
            ]);
        File.WriteAllText(Path.Combine(indexDir, "documents.leann.meta.json"), JsonSerializer.Serialize(meta));

        var jsonl = new StringBuilder();
        foreach (var p in passages) jsonl.AppendLine(JsonSerializer.Serialize(new PassageData(p.Id, p.Text, null)));
        File.WriteAllText(Path.Combine(indexDir, "documents.leann.passages.jsonl"), jsonl.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(indexDir, "documents.ids.txt"), string.Join('\n', passages.Select(p => p.Id)) + "\n");

        using var stream = File.Create(Path.Combine(indexDir, "documents.embeddings.bin"));
        foreach (var p in passages)
            stream.Write(MemoryMarshal.AsBytes(Axis(model.Dimensions, p.Axis).AsSpan()));
    }

    private IndexManager Manager(params (string Query, int Axis)[] queries) => new(
        new StaticFactory(new AxisStubService(_model.Dimensions, queries.ToDictionary(q => q.Query, q => q.Axis))),
        NullLogger<IndexManager>.Instance,
        _indexesDir);

    [Theory]
    [InlineData("all", true)]
    [InlineData("ALL", true)]
    [InlineData("*", true)]
    [InlineData("Workflow__*", true)]
    [InlineData("a,b", true)]
    [InlineData("!tests__*", true)]
    [InlineData("Workflow__Hyland.Workflow.Core", false)]
    [InlineData("my-repo", false)]
    public void IsMultiIndexSelector_RecognisesSelectors(string selector, bool expected) =>
        Assert.Equal(expected, IndexManager.IsMultiIndexSelector(selector));

    [Theory]
    [InlineData("all", "Libraries__Core,Libraries__Unity,tests__Core,Workflow__Core")]
    [InlineData("*", "Libraries__Core,Libraries__Unity,tests__Core,Workflow__Core")]
    [InlineData("*,!tests__*", "Libraries__Core,Libraries__Unity,Workflow__Core")]
    [InlineData("!tests__*", "Libraries__Core,Libraries__Unity,Workflow__Core")]
    [InlineData("libraries__*", "Libraries__Core,Libraries__Unity")]
    [InlineData("Workflow__Core, Libraries__Unity", "Libraries__Unity,Workflow__Core")]
    [InlineData("*__Core,!Libraries__*", "tests__Core,Workflow__Core")]
    [InlineData("Libraries__C?re", "Libraries__Core")]
    [InlineData("Nothing__*", "")]
    public void ResolveSelector_AppliesIncludesThenExcludes(string selector, string expected)
    {
        string[] available = ["Libraries__Core", "Libraries__Unity", "tests__Core", "Workflow__Core"];

        var resolved = IndexManager.ResolveSelector(selector, available);

        Assert.Equal(expected, string.Join(',', resolved));
    }

    [Fact]
    public void SearchAll_RanksByCosineAcrossIndexes_AndNamesEachResultsIndex()
    {
        WriteIndex("alpha", ("a1", "alpha first", 1), ("a2", "alpha second", 2));
        WriteIndex("beta", ("b1", "beta first", 3), ("b2", "beta second", 4));
        var manager = Manager(("which passage", 3));

        var result = manager.Search("all", "which passage", topK: 2, dedupThreshold: 0.0);

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.Error);
        Assert.Equal(("b1", "beta"), (result.Value[0].Id, result.Value[0].IndexName));
    }

    [Fact]
    public void SearchAll_PinsExactIdentifierFromWhicheverIndexHoldsIt()
    {
        WriteIndex("alpha", ("a1", "patient banner configuration", 1), ("a2", "ThumbnailURICacheExpirationHours controls refresh", 2));
        WriteIndex("beta", ("b1", "session inactivity timeout", 3), ("b2", "document type tab assignment", 4));
        // The query embeds closest to a beta passage; the identifier pin must still win.
        var manager = Manager(("ThumbnailURICacheExpirationHours", 3));

        var result = manager.Search("*", "ThumbnailURICacheExpirationHours", topK: 3, dedupThreshold: 0.0);

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.Error);
        Assert.Equal(("a2", "alpha"), (result.Value[0].Id, result.Value[0].IndexName));
    }

    [Fact]
    public void SearchAll_KeepsPassagesThatShareAnIdInDifferentIndexesApart()
    {
        WriteIndex("alpha", ("0", "alpha passage", 1));
        WriteIndex("beta", ("0", "beta passage", 2));
        var manager = Manager();

        var result = manager.Search("alpha,beta", "unrelated words", topK: 5, dedupThreshold: 0.0);

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.Error);
        Assert.Equal(["alpha passage", "beta passage"], result.Value.Select(r => r.Text).Order());
        Assert.Equal(["alpha", "beta"], result.Value.Select(r => r.IndexName!).Order());
    }

    [Fact]
    public void SearchAll_ExcludedIndexesContributeNothing()
    {
        WriteIndex("alpha", ("a1", "alpha passage", 1));
        WriteIndex("tests__alpha", ("t1", "test passage", 1));
        var manager = Manager(("query", 1));

        var result = manager.Search("*,!tests__*", "query", topK: 5, dedupThreshold: 0.0);

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.Error);
        Assert.All(result.Value, r => Assert.Equal("alpha", r.IndexName));
    }

    [Fact]
    public void SearchAll_DeduplicatesNearIdenticalPassagesAcrossIndexes()
    {
        WriteIndex("alpha", ("a1", "shared helper copied into two projects", 1), ("a2", "other alpha code", 5));
        WriteIndex("beta", ("b1", "shared helper copied into two projects", 1), ("b2", "other beta code", 6));
        var manager = Manager(("query", 1));

        var result = manager.Search("all", "query", topK: 3, dedupThreshold: 0.95);

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.Error);
        Assert.Single(result.Value, r => r.Text == "shared helper copied into two projects");
    }

    /// <summary>Embeds every query as the same vector, which leans toward the low axes.</summary>
    private sealed class FixedStubService(float[] vector) : IEmbeddingService
    {
        public Result<float[]> ComputeEmbedding(string text) => vector;

        public Result<float[][]> ComputeEmbeddings(IReadOnlyList<string> texts) => texts.Select(_ => vector).ToArray();

        public void Warmup() { }
    }

    [Theory]
    [InlineData("how is the retry delay configured for failed uploads")]
    [InlineData("UploadRetryPolicy")] // three passages per side: rare in each index, too common in both (no pin)
    [InlineData("ArchiveCompactor schedule")] // only on the left: pinned
    public void SearchAcrossIndexes_RanksExactlyLikeOneMergedIndex(string query)
    {
        (string Id, string Text, int Axis)[] left =
        [
            ("l1", "UploadRetryPolicy sets the retry delay for failed uploads", 1),
            ("l2", "UploadRetryPolicy backoff grows after each failed attempt of the upload queue", 3),
            ("l3", "configure UploadRetryPolicy in the server settings file", 5),
            ("l4", "ArchiveCompactor runs on a nightly schedule and merges small archives", 7),
            ("l5", "patient banner shows demographics", 9),
        ];
        (string Id, string Text, int Axis)[] right =
        [
            ("r1", "UploadRetryPolicy is read when an upload fails", 2),
            ("r2", "the retry delay is configured in seconds", 4),
            ("r3", "UploadRetryPolicy defaults apply when no policy is configured for uploads", 6),
            ("r4", "UploadRetryPolicy logging records each delay", 8),
            ("r5", "document viewer toolbar layout and icons", 10),
        ];
        WriteIndex("left", left);
        WriteIndex("right", right);
        WriteIndex("merged", [.. left, .. right]);
        var queryVector = new float[_model.Dimensions];
        for (var axis = 1; axis <= 10; axis++) queryVector[axis] = 1.0f / axis;
        var manager = new IndexManager(
            new StaticFactory(new FixedStubService(queryVector)), NullLogger<IndexManager>.Instance, _indexesDir);

        var split = manager.Search("left,right", query, topK: 10, complexity: 10, dedupThreshold: 0.0);
        var merged = manager.Search("merged", query, topK: 10, complexity: 10, dedupThreshold: 0.0);

        Assert.True(split.IsSuccess, split.IsSuccess ? "" : split.Error);
        Assert.True(merged.IsSuccess, merged.IsSuccess ? "" : merged.Error);
        Assert.Equal(merged.Value.Select(r => r.Id), split.Value.Select(r => r.Id));
        Assert.Equal(merged.Value.Select(r => r.Score), split.Value.Select(r => r.Score));
    }

    [Fact]
    public void SearchAll_NoMatchingIndex_Fails()
    {
        WriteIndex("alpha", ("a1", "alpha passage", 1));

        var result = Manager().Search("Nothing__*", "query");

        Assert.True(result.IsFailure);
        Assert.Contains("No index matches", result.Error);
    }

    [Fact]
    public void SearchAll_IndexesWithDifferentModels_Fails()
    {
        var other = ModelRegistry.All.First(m => m.Id != _model.Id && m.Dimensions == _model.Dimensions);
        WriteIndex("alpha", ("a1", "alpha passage", 1));
        WriteIndex("beta", other, ("b1", "beta passage", 1));

        var result = Manager().Search("all", "query");

        Assert.True(result.IsFailure);
        Assert.Contains("different embedding models", result.Error);
    }

    [Fact]
    public void SingleIndexSearch_LeavesIndexNameUnset()
    {
        WriteIndex("alpha", ("a1", "alpha passage", 1));

        var result = Manager(("query", 1)).Search("alpha", "query", dedupThreshold: 0.0);

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.Error);
        Assert.Null(Assert.Single(result.Value).IndexName);
    }

    [Fact]
    public void IndexNamedLikeASelector_IsStillSearchedByName()
    {
        WriteIndex("all", ("x1", "literal all index", 1));
        WriteIndex("beta", ("b1", "beta passage", 1));

        var result = Manager(("query", 1)).Search("all", "query", topK: 5, dedupThreshold: 0.0);

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.Error);
        var hit = Assert.Single(result.Value);
        Assert.Equal("x1", hit.Id);
        Assert.Null(hit.IndexName);
    }
}
