using CSharpFunctionalExtensions;

namespace LeannMcp.Services;

public interface IEmbeddingService
{
    Result<float[]> ComputeEmbedding(string text);
    Result<float[][]> ComputeEmbeddings(IReadOnlyList<string> texts);
    void Warmup();
}
