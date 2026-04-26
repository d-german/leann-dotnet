using CSharpFunctionalExtensions;

namespace LeannMcp.Services;

public interface IVectorIndex
{
    Result<IReadOnlyList<(string Id, float Score)>> Search(float[] queryEmbedding, int topK);
    int Count { get; }

    /// <summary>
    /// Returns the L2-normalized embedding stored for <paramref name="id"/>,
    /// or null if no entry exists. Used by <see cref="NearDuplicateFilter"/>
    /// to compare candidate hits against already-kept hits without re-running
    /// search. Implementations that don't keep raw embeddings in memory are
    /// free to return null — the dedup filter degrades gracefully (keeps the
    /// hit rather than dropping it).
    /// </summary>
    float[]? TryGetEmbedding(string id);
}
