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
            DownloadUrl: "", // populated in T14 after release upload
            ArchiveType: ArchiveType.Zip,
            OnnxFilename: "model.onnx",
            TokenizerType: TokenizerType.RobertaBpe,
            Dimensions: 768,
            Pooling: Pooling.Mean,
            MaxSequenceLength: 8192,
            License: "Apache-2.0",
            Sha256: ""), // populated in T14
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
