using CSharpFunctionalExtensions;

namespace LeannMcp.Services;

public interface IVectorIndex
{
    Result<IReadOnlyList<(string Id, float Score)>> Search(float[] queryEmbedding, int topK);
    int Count { get; }
}
