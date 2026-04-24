using LeannMcp.Models;
using LeannMcp.Services;
using Xunit;

namespace LeannMcp.Tests;

public class IndexManagerModelGuardTests
{
    private static readonly EmbeddingModelDescriptor Jina = ModelRegistry.GetById(ModelRegistry.JinaCodeId).Value;
    private static readonly EmbeddingModelDescriptor Contriever = ModelRegistry.GetById(ModelRegistry.ContrieverId).Value;

    private static IndexMetadata MakeMetadata(string? embeddingModel, int dimensions = 768) =>
        new(
            Version: "1.0",
            BackendName: "flat",
            EmbeddingModel: embeddingModel!,
            Dimensions: dimensions,
            EmbeddingMode: null,
            PassageSources: new List<PassageSource>());

    [Fact]
    public void EnsureCompatibleModel_ContrieverIndex_JinaActive_Fails()
    {
        var meta = MakeMetadata(ModelRegistry.ContrieverId);

        var result = IndexCompatibility.EnsureCompatibleModel(meta, Jina, "/some/index");

        Assert.True(result.IsFailure);
        Assert.Contains(ModelRegistry.ContrieverId, result.Error);
        Assert.Contains(ModelRegistry.JinaCodeId, result.Error);
        Assert.Contains("LEANN_MODEL=" + ModelRegistry.ContrieverId, result.Error);
    }

    [Fact]
    public void EnsureCompatibleModel_ContrieverIndex_ContrieverActive_Succeeds()
    {
        var meta = MakeMetadata(ModelRegistry.ContrieverId);

        var result = IndexCompatibility.EnsureCompatibleModel(meta, Contriever, "/some/index");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void EnsureCompatibleModel_JinaIndex_JinaActive_Succeeds()
    {
        var meta = MakeMetadata(ModelRegistry.JinaCodeId);

        var result = IndexCompatibility.EnsureCompatibleModel(meta, Jina, "/some/index");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void EnsureCompatibleModel_NullEmbeddingModel_Fails()
    {
        var meta = MakeMetadata(null);

        var result = IndexCompatibility.EnsureCompatibleModel(meta, Jina, "/legacy/index");

        Assert.True(result.IsFailure);
        Assert.Contains(ModelRegistry.JinaCodeId, result.Error);
    }

    [Fact]
    public void EnsureCompatibleModel_EmptyEmbeddingModel_Fails()
    {
        var meta = MakeMetadata(string.Empty);

        var result = IndexCompatibility.EnsureCompatibleModel(meta, Jina, "/legacy/index");

        Assert.True(result.IsFailure);
        Assert.Contains(ModelRegistry.JinaCodeId, result.Error);
    }

    [Fact]
    public void EnsureCompatibleModel_DimensionMismatchSameId_Fails()
    {
        var meta = MakeMetadata(ModelRegistry.JinaCodeId, dimensions: 384);

        var result = IndexCompatibility.EnsureCompatibleModel(meta, Jina, "/corrupt/index");

        Assert.True(result.IsFailure);
        Assert.Contains("Dimension mismatch", result.Error);
    }
}
