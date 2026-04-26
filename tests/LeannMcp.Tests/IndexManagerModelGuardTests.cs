using LeannMcp.Models;
using LeannMcp.Services;
using Xunit;

namespace LeannMcp.Tests;

/// <summary>
/// Post-v2.4.0 contract: IndexCompatibility no longer compares against an "active" descriptor.
/// Per-index model selection means id mismatch is expected and OK; only dim mismatch is fatal.
/// </summary>
public class IndexManagerModelGuardTests
{
    private static readonly EmbeddingModelDescriptor Jina = ModelRegistry.GetById(ModelRegistry.JinaCodeId).Value;

    private static IndexMetadata MakeMetadata(string embeddingModel, int dimensions = 768) =>
        new(
            Version: "1.0",
            BackendName: "flat",
            EmbeddingModel: embeddingModel,
            Dimensions: dimensions,
            EmbeddingMode: null,
            PassageSources: new List<PassageSource>());

    [Fact]
    public void EnsureManifestIntegrity_DimensionsMatch_Succeeds()
    {
        var meta = MakeMetadata(ModelRegistry.JinaCodeId, dimensions: 768);

        var result = IndexCompatibility.EnsureManifestIntegrity(meta, Jina, "/some/index");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void EnsureManifestIntegrity_DimensionsMismatch_Fails()
    {
        var meta = MakeMetadata(ModelRegistry.JinaCodeId, dimensions: 384);

        var result = IndexCompatibility.EnsureManifestIntegrity(meta, Jina, "/corrupt/index");

        Assert.True(result.IsFailure);
        Assert.Contains("dim=384", result.Error);
        Assert.Contains("dim=768", result.Error);
        Assert.Contains("--rebuild", result.Error);
    }

    [Fact]
    public void EnsureManifestIntegrity_EmbeddingsMetaModelMismatch_Fails()
    {
        var dir = Path.Combine(Path.GetTempPath(), "leann-compat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(
                Path.Combine(dir, "documents.embeddings.meta.json"),
                """{"count":1,"dimensions":768,"normalized":true,"model_id":"facebook/contriever"}""");
            var meta = MakeMetadata(ModelRegistry.JinaCodeId, dimensions: 768);

            var result = IndexCompatibility.EnsureManifestIntegrity(meta, Jina, dir);

            Assert.True(result.IsFailure);
            Assert.Contains("embeddings metadata", result.Error);
            Assert.Contains("facebook/contriever", result.Error);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
