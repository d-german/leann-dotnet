using CSharpFunctionalExtensions;

namespace LeannMcp.Models;

public static class ModelRegistry
{
    public const string JinaCodeId = "jinaai/jina-embeddings-v2-base-code";
    public const string ContrieverId = "facebook/contriever";

    public static IReadOnlyList<EmbeddingModelDescriptor> All { get; } = new[]
    {
        new EmbeddingModelDescriptor(
            Id: JinaCodeId,
            DisplayName: "Jina Embeddings v2 Base Code",
            DownloadUrl: "https://github.com/d-german/leann-dotnet/releases/download/model-v2-jina/jina-embeddings-v2-base-code-onnx.zip",
            ArchiveType: ArchiveType.Zip,
            OnnxFilename: "model.onnx",
            TokenizerType: TokenizerType.RobertaBpe,
            Dimensions: 768,
            Pooling: Pooling.Mean,
            MaxSequenceLength: 8192,
            License: "Apache-2.0",
            Sha256: "40FE6A1BFA279229F76E231520F4396300DC4B6046223E0EE9C6BB9D1F653624"),
        new EmbeddingModelDescriptor(
            Id: ContrieverId,
            DisplayName: "Facebook Contriever",
            DownloadUrl: "https://github.com/d-german/leann-dotnet/releases/download/model-v1/contriever-onnx.zip",
            ArchiveType: ArchiveType.Zip,
            OnnxFilename: "model.onnx",
            TokenizerType: TokenizerType.WordPiece,
            Dimensions: 768,
            Pooling: Pooling.Mean,
            MaxSequenceLength: 512,
            License: "Apache-2.0",
            Sha256: ""),
    };

    public static Maybe<EmbeddingModelDescriptor> GetById(string id) =>
        All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    public static EmbeddingModelDescriptor Default =>
        All.First(m => m.Id == JinaCodeId);
}
