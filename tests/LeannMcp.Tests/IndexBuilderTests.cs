using CSharpFunctionalExtensions;
using LeannMcp.Models;
using LeannMcp.Services;
using LeannMcp.Services.Chunking;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace LeannMcp.Tests;

public sealed class IndexBuilderTests : IDisposable
{
    private readonly string _root;
    private readonly string _indexesDir;

    public IndexBuilderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "leann-builder-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        _indexesDir = Path.Combine(_root, ".leann", "indexes");
        Directory.CreateDirectory(_indexesDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void BuildAll_UsesModelFromEachIndexManifest()
    {
        var jina = ModelRegistry.GetById(ModelRegistry.JinaCodeId).Value;
        var contriever = ModelRegistry.GetById(ModelRegistry.ContrieverId).Value;
        var codeDir = WriteIndex("code", jina);
        var docsDir = WriteIndex("docs", contriever);
        var factory = new RecordingFactory();
        var builder = new IndexBuilder(factory, NullLogger<IndexBuilder>.Instance);

        var result = builder.BuildAll(_indexesDir, batchSize: 1, force: true);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "");
        Assert.Contains(jina.Id, factory.RequestedModelIds);
        Assert.Contains(contriever.Id, factory.RequestedModelIds);

        Assert.Equal(1.0f, ReadFloat(codeDir, offset: 0));
        Assert.Equal(0.0f, ReadFloat(codeDir, offset: sizeof(float)));
        Assert.Equal(0.0f, ReadFloat(docsDir, offset: 0));
        Assert.Equal(1.0f, ReadFloat(docsDir, offset: sizeof(float)));
        Assert.Equal(jina.Id, ReadEmbeddingModelId(codeDir));
        Assert.Equal(contriever.Id, ReadEmbeddingModelId(docsDir));
    }

    [Fact]
    public void BuildIndex_IdsFileReferencesMissingPassage_Fails()
    {
        var indexDir = WriteIndex("broken", ModelRegistry.GetById(ModelRegistry.JinaCodeId).Value);
        File.WriteAllText(Path.Combine(indexDir, "documents.ids.txt"), "missing\n");
        var builder = new IndexBuilder(new RecordingFactory(), NullLogger<IndexBuilder>.Instance);

        var result = builder.BuildIndex(indexDir, "broken", batchSize: 1, force: true);

        Assert.True(result.IsFailure);
        Assert.Contains("missing", result.Error);
    }

    [Fact]
    public void BuildIndex_ForceWithStaleEmbeddingsMeta_RewritesMetadata()
    {
        var jina = ModelRegistry.GetById(ModelRegistry.JinaCodeId).Value;
        var indexDir = WriteIndex("stale", jina);
        File.WriteAllText(
            Path.Combine(indexDir, "documents.embeddings.meta.json"),
            """{"count":1,"dimensions":768,"normalized":true,"model_id":"facebook/contriever"}""");
        var builder = new IndexBuilder(new RecordingFactory(), NullLogger<IndexBuilder>.Instance);

        var result = builder.BuildIndex(indexDir, "stale", batchSize: 1, force: true);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "");
        Assert.Equal(jina.Id, ReadEmbeddingModelId(indexDir));
    }

    private string WriteIndex(string name, EmbeddingModelDescriptor descriptor)
    {
        var indexDir = Path.Combine(_indexesDir, name);
        var writer = new PassageWriter(NullLogger<PassageWriter>.Instance, descriptor);
        var passages = new List<PassageData> { new("0", $"passage for {name}") };
        var result = writer.WritePassages(indexDir, name, passages, new[] { indexDir });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "");
        return indexDir;
    }

    private static float ReadFloat(string indexDir, int offset)
    {
        var bytes = File.ReadAllBytes(Path.Combine(indexDir, "documents.embeddings.bin"));
        return BitConverter.ToSingle(bytes, offset);
    }

    private static string? ReadEmbeddingModelId(string indexDir)
    {
        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(indexDir, "documents.embeddings.meta.json")));
        return doc.RootElement.GetProperty("model_id").GetString();
    }

    private sealed class RecordingFactory : IEmbeddingServiceFactory
    {
        private readonly List<string> _requested = [];

        public IReadOnlyCollection<string> LoadedModelIds => _requested.ToArray();
        public IReadOnlyList<string> RequestedModelIds => _requested;

        public Result<IEmbeddingService> GetOrCreate(EmbeddingModelDescriptor descriptor)
        {
            _requested.Add(descriptor.Id);
            var hotDimension = descriptor.Id == ModelRegistry.JinaCodeId ? 0 : 1;
            return Result.Success<IEmbeddingService>(new ConstantEmbeddingService(hotDimension));
        }
    }

    private sealed class ConstantEmbeddingService(int hotDimension) : IEmbeddingService
    {
        public Result<float[]> ComputeEmbedding(string text) => MakeVector();

        public Result<float[][]> ComputeEmbeddings(IReadOnlyList<string> texts) =>
            Result.Success(texts.Select(_ => MakeVector().Value).ToArray());

        public void Warmup() { }

        private Result<float[]> MakeVector()
        {
            var vector = new float[768];
            vector[hotDimension] = 1.0f;
            return vector;
        }
    }
}
