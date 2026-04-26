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
/// End-to-end test: an IndexManager loaded against a fixture index demonstrates
/// that BM25 lexical recall fuses with dense cosine ranking and surfaces the
/// passage containing an exact CamelCase identifier — the precise failure
/// mode T24 documented for pure-dense Contriever retrieval.
/// </summary>
public sealed class IndexManagerHybridSearchTests : IDisposable
{
    private readonly string _root;
    private readonly string _indexesDir;

    public IndexManagerHybridSearchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "leann-hybrid-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        _indexesDir = Path.Combine(_root, ".leann", "indexes");
        Directory.CreateDirectory(_indexesDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class UniformStubService(int dim) : IEmbeddingService
    {
        // Returns the same vector regardless of input → all docs score equally
        // under cosine, so ordering is decided entirely by BM25 + RRF.
        public Result<float[]> ComputeEmbedding(string text)
        {
            var v = new float[dim];
            v[0] = 1.0f;
            return v;
        }

        public Result<float[][]> ComputeEmbeddings(IReadOnlyList<string> texts) =>
            texts.Select(_ => ComputeEmbedding(_).Value).ToArray();

        public void Warmup() { }
    }

    private sealed class StaticFactory(IEmbeddingService service) : IEmbeddingServiceFactory
    {
        public Result<IEmbeddingService> GetOrCreate(EmbeddingModelDescriptor descriptor)
            => Result.Success(service);

        public IReadOnlyCollection<string> LoadedModelIds => Array.Empty<string>();
    }

    private void WriteFixtureIndex(string indexName, EmbeddingModelDescriptor model, IReadOnlyList<PassageData> passages)
    {
        var indexDir = Path.Combine(_indexesDir, indexName);
        Directory.CreateDirectory(indexDir);

        var meta = new IndexMetadata(
            Version: "1.0",
            BackendName: "flat",
            EmbeddingModel: model.Id,
            Dimensions: model.Dimensions,
            EmbeddingMode: null,
            PassageSources: new List<PassageSource>
            {
                new(Type: "jsonl", Path: null, IndexPath: null, PathRelative: "documents.leann.passages.jsonl", IndexPathRelative: null),
            });
        File.WriteAllText(Path.Combine(indexDir, "documents.leann.meta.json"), JsonSerializer.Serialize(meta));

        var jsonl = new StringBuilder();
        foreach (var p in passages) jsonl.AppendLine(JsonSerializer.Serialize(p));
        File.WriteAllText(Path.Combine(indexDir, "documents.leann.passages.jsonl"), jsonl.ToString(), new UTF8Encoding(false));

        var ids = string.Join('\n', passages.Select(p => p.Id)) + "\n";
        File.WriteAllText(Path.Combine(indexDir, "documents.ids.txt"), ids);

        // All embeddings identical → dense gives no preference; BM25 decides.
        var rowBytes = model.Dimensions * sizeof(float);
        using var stream = File.Create(Path.Combine(indexDir, "documents.embeddings.bin"));
        foreach (var _ in passages)
        {
            var emb = new float[model.Dimensions];
            emb[0] = 1.0f;
            stream.Write(MemoryMarshal.AsBytes(emb.AsSpan()));
        }
    }

    [Fact]
    public void HybridSearch_ExactCamelCaseQuery_SurfacesContainingPassageInTop3()
    {
        var model = ModelRegistry.GetById(ModelRegistry.ContrieverId).Value;
        var passages = new[]
        {
            new PassageData("noise1", "patient banner missing data groups configuration", null),
            new PassageData("noise2", "patient variable list age birthDate gender medicalRecord", null),
            new PassageData("target", "ThumbnailURICacheExpirationHours determines how long cached URIs persist before refresh", null),
            new PassageData("noise3", "full text search description supports prefix queries", null),
            new PassageData("noise4", "session inactivity timeout settings reset interval", null),
        };
        WriteFixtureIndex("hybrid-idx", model, passages);

        var manager = new IndexManager(
            new StaticFactory(new UniformStubService(model.Dimensions)),
            NullLogger<IndexManager>.Instance,
            _indexesDir);

        var result = manager.Search("hybrid-idx", "ThumbnailURICacheExpirationHours", topK: 3, dedupThreshold: 0.0);

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.Error);
        Assert.NotEmpty(result.Value);
        Assert.Equal("target", result.Value[0].Id);
    }

    [Fact]
    public void HybridSearch_NaturalLanguageQuery_AlsoSurfacesContainingPassage()
    {
        var model = ModelRegistry.GetById(ModelRegistry.ContrieverId).Value;
        var passages = new[]
        {
            new PassageData("a", "patient banner configuration with grouping options", null),
            new PassageData("b", "ThumbnailURICacheExpirationHours setting controls thumbnail cache expiration", null),
            new PassageData("c", "session inactivity timeout reset behaviour", null),
        };
        WriteFixtureIndex("hybrid-nl", model, passages);

        var manager = new IndexManager(
            new StaticFactory(new UniformStubService(model.Dimensions)),
            NullLogger<IndexManager>.Instance,
            _indexesDir);

        var result = manager.Search("hybrid-nl", "thumbnail cache expiration", topK: 3, dedupThreshold: 0.0);

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.Error);
        Assert.Equal("b", result.Value[0].Id);
    }

    [Fact]
    public void HybridSearch_IdentifierBoost_BeatsHighFrequencyComponentCompetitor()
    {
        // Live T25 probe-2 failure mode reproduced as an integration test:
        //   competitor chunk spams 'Thumbnail' multiple times across natural
        //   prose; target chunk contains the exact CamelCase identifier once.
        // With the T26 IdentifierBoost the target wins rank #1.
        var model = ModelRegistry.GetById(ModelRegistry.ContrieverId).Value;
        var passages = new[]
        {
            new PassageData(
                "competitor",
                "Thumbnail rendering pipeline. The Thumbnail generator produces " +
                "Thumbnail previews for documents. Thumbnail caching is handled " +
                "downstream and Thumbnail invalidation is automatic.",
                null),
            new PassageData(
                "target",
                "ThumbnailURICacheExpirationHours determines how long the BFF " +
                "caches generated thumbnail URIs before refreshing them.",
                null),
            new PassageData("noise1", "session inactivity timeout reset behaviour", null),
            new PassageData("noise2", "patient banner configuration with grouping options", null),
        };
        WriteFixtureIndex("hybrid-boost", model, passages);

        var manager = new IndexManager(
            new StaticFactory(new UniformStubService(model.Dimensions)),
            NullLogger<IndexManager>.Instance,
            _indexesDir);

        var result = manager.Search("hybrid-boost", "ThumbnailURICacheExpirationHours", topK: 4, dedupThreshold: 0.0);

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.Error);
        Assert.Equal("target", result.Value[0].Id);
    }

    [Fact]
    public void HybridSearch_DenseRanksTargetOutsideWindow_PinningStillWins()
    {
        // Live T26 probe-B failure mode: dense ranker (Contriever on identifier
        // queries) ranks the target chunk so far down that it falls OUTSIDE the
        // over-fetch window, contributing 0 to RRF. The unique-identifier pin
        // makes this case rank-1 anyway because the identifier has df=1.
        var model = ModelRegistry.GetById(ModelRegistry.ContrieverId).Value;

        // 'target' contains the unique identifier; competitors contain
        // 'thumbnail' multiple times so RRF would otherwise prefer them.
        var passages = new[]
        {
            new PassageData("competitor", "Thumbnail thumbnail Thumbnail rendering pipeline overview", null),
            new PassageData("target", "ThumbnailURICacheExpirationHours sets the URI cache lifetime", null),
            new PassageData("noise", "Some other section about logging and telemetry", null),
        };

        // Write fixture, but use ORTHOGONAL embeddings so 'target' gets dense
        // score 0 and competitor gets dense score 1.0 — guaranteeing target is
        // last in the dense ranking.
        var indexDir = Path.Combine(_indexesDir, "pin-test");
        Directory.CreateDirectory(indexDir);

        var meta = new IndexMetadata(
            Version: "1.0",
            BackendName: "flat",
            EmbeddingModel: model.Id,
            Dimensions: model.Dimensions,
            EmbeddingMode: null,
            PassageSources: new List<PassageSource>
            {
                new(Type: "jsonl", Path: null, IndexPath: null,
                    PathRelative: "documents.leann.passages.jsonl", IndexPathRelative: null),
            });
        File.WriteAllText(Path.Combine(indexDir, "documents.leann.meta.json"),
            System.Text.Json.JsonSerializer.Serialize(meta));

        var jsonl = new System.Text.StringBuilder();
        foreach (var p in passages) jsonl.AppendLine(System.Text.Json.JsonSerializer.Serialize(p));
        File.WriteAllText(Path.Combine(indexDir, "documents.leann.passages.jsonl"),
            jsonl.ToString(), new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(indexDir, "documents.ids.txt"),
            string.Join('\n', passages.Select(p => p.Id)) + "\n");

        // Write embeddings: competitor=axis0, noise=axis1, target=axis2.
        // Stub query returns axis0 → competitor dense rank #1, target rank LAST.
        using (var stream = File.Create(Path.Combine(indexDir, "documents.embeddings.bin")))
        {
            for (var i = 0; i < passages.Length; i++)
            {
                var emb = new float[model.Dimensions];
                emb[i] = 1.0f;
                stream.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(emb.AsSpan()));
            }
        }

        var manager = new IndexManager(
            new StaticFactory(new UniformStubService(model.Dimensions)),
            NullLogger<IndexManager>.Instance,
            _indexesDir);

        var result = manager.Search("pin-test", "ThumbnailURICacheExpirationHours", topK: 3, dedupThreshold: 0.0);

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.Error);
        Assert.Equal("target", result.Value[0].Id);
    }
}
