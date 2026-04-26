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
/// Integration test: one IndexManager serves two on-disk indexes built with different model ids.
/// Each Search call must route through the correct stub embedding service — proving per-index
/// model selection works end-to-end inside IndexManager.LoadIndex + ExecuteSearch.
/// </summary>
public sealed class IndexManagerMultiModelTests : IDisposable
{
    private readonly string _root;
    private readonly string _indexesDir;

    public IndexManagerMultiModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "leann-mm-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        _indexesDir = Path.Combine(_root, ".leann", "indexes");
        Directory.CreateDirectory(_indexesDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class TaggedStubService : IEmbeddingService
    {
        public string Tag { get; }
        public int CallCount { get; private set; }
        private readonly int _dim;

        public TaggedStubService(string tag, int dim)
        {
            Tag = tag;
            _dim = dim;
        }

        public Result<float[]> ComputeEmbedding(string text)
        {
            CallCount++;
            // Deterministic vector matching the index's only stored row so cosine = 1.
            var v = new float[_dim];
            v[0] = 1.0f;
            return v;
        }

        public Result<float[][]> ComputeEmbeddings(IReadOnlyList<string> texts)
            => texts.Select(_ => ComputeEmbedding(_).Value).ToArray();

        public void Warmup() { }
    }

    private sealed class RoutingFactory : IEmbeddingServiceFactory
    {
        private readonly Dictionary<string, IEmbeddingService> _byId;
        public RoutingFactory(Dictionary<string, IEmbeddingService> byId) => _byId = byId;

        public Result<IEmbeddingService> GetOrCreate(EmbeddingModelDescriptor descriptor)
            => _byId.TryGetValue(descriptor.Id, out var s)
                ? Result.Success(s)
                : Result.Failure<IEmbeddingService>($"unknown model {descriptor.Id}");

        public IReadOnlyCollection<string> LoadedModelIds => _byId.Keys.ToArray();
    }

    private void WriteFixtureIndex(string indexName, string modelId, int dim)
    {
        var indexDir = Path.Combine(_indexesDir, indexName);
        Directory.CreateDirectory(indexDir);

        var meta = new IndexMetadata(
            Version: "1.0",
            BackendName: "flat",
            EmbeddingModel: modelId,
            Dimensions: dim,
            EmbeddingMode: null,
            PassageSources: new List<PassageSource>
            {
                new(Type: "jsonl", Path: null, IndexPath: null, PathRelative: "documents.leann.passages.jsonl", IndexPathRelative: null)
            });
        File.WriteAllText(Path.Combine(indexDir, "documents.leann.meta.json"), JsonSerializer.Serialize(meta));

        // Single passage with id matching the embedding row.
        var passage = new PassageData($"{indexName}-p0", $"hello from {indexName}");
        File.WriteAllText(
            Path.Combine(indexDir, "documents.leann.passages.jsonl"),
            JsonSerializer.Serialize(passage) + "\n",
            new UTF8Encoding(false));

        File.WriteAllText(Path.Combine(indexDir, "documents.ids.txt"), $"{indexName}-p0\n");

        var emb = new float[dim];
        emb[0] = 1.0f; // matches the stub query vector
        var bytes = MemoryMarshal.AsBytes(emb.AsSpan()).ToArray();
        File.WriteAllBytes(Path.Combine(indexDir, "documents.embeddings.bin"), bytes);
    }

    [Fact]
    public void TwoIndexes_DifferentModels_RouteToCorrectService()
    {
        // Two indexes built with two different real-registry models (different dims).
        var jina = ModelRegistry.GetById(ModelRegistry.JinaCodeId).Value;
        var contriever = ModelRegistry.GetById(ModelRegistry.ContrieverId).Value;
        WriteFixtureIndex("code-idx", jina.Id, jina.Dimensions);
        WriteFixtureIndex("docs-idx", contriever.Id, contriever.Dimensions);

        var jinaSvc = new TaggedStubService("jina", jina.Dimensions);
        var contrieverSvc = new TaggedStubService("contriever", contriever.Dimensions);
        var factory = new RoutingFactory(new Dictionary<string, IEmbeddingService>
        {
            [jina.Id] = jinaSvc,
            [contriever.Id] = contrieverSvc,
        });

        var manager = new IndexManager(factory, NullLogger<IndexManager>.Instance, _indexesDir);

        var codeResult = manager.Search("code-idx", "anything", topK: 1);
        var docsResult = manager.Search("docs-idx", "anything", topK: 1);

        Assert.True(codeResult.IsSuccess, codeResult.IsSuccess ? "" : codeResult.Error);
        Assert.True(docsResult.IsSuccess, docsResult.IsSuccess ? "" : docsResult.Error);

        // Each stub service was invoked exactly once for its own index — proves per-index routing.
        Assert.Equal(1, jinaSvc.CallCount);
        Assert.Equal(1, contrieverSvc.CallCount);

        Assert.Single(codeResult.Value);
        Assert.Equal("code-idx-p0", codeResult.Value[0].Id);
        Assert.Single(docsResult.Value);
        Assert.Equal("docs-idx-p0", docsResult.Value[0].Id);
    }

    [Fact]
    public void UnknownModelInManifest_Fails_WithRegistryHint()
    {
        WriteFixtureIndex("alien-idx", "vendor/not-registered", 768);
        var factory = new RoutingFactory(new Dictionary<string, IEmbeddingService>());
        var manager = new IndexManager(factory, NullLogger<IndexManager>.Instance, _indexesDir);

        var result = manager.Search("alien-idx", "x", topK: 1);

        Assert.True(result.IsFailure);
        Assert.Contains("vendor/not-registered", result.Error);
        Assert.Contains("ModelRegistry", result.Error);
    }
}
