using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.InteropServices;
using CSharpFunctionalExtensions;
using LeannMcp.Infrastructure;

namespace LeannMcp.Services;

/// <summary>
/// Brute-force cosine similarity search over pre-computed, L2-normalized embeddings.
/// Since embeddings are pre-normalized, cosine similarity = dot product.
/// Uses SIMD for vectorized dot products.
/// </summary>
public sealed class FlatVectorIndex : IVectorIndex
{
    private readonly float[][] _embeddings;
    private readonly string[] _ids;
    private readonly int _dimensions;

    public int Count => _embeddings.Length;

    public FlatVectorIndex(string embeddingsPath, string idsPath, int dimensions)
    {
        _dimensions = dimensions;
        _ids = LoadIds(idsPath);
        _embeddings = LoadEmbeddings(embeddingsPath, _ids.Length, dimensions);
    }

    public Result<IReadOnlyList<(string Id, float Score)>> Search(float[] queryEmbedding, int topK)
    {
        if (queryEmbedding.Length != _dimensions)
            return Result.Failure<IReadOnlyList<(string, float)>>(
                $"Query dimension {queryEmbedding.Length} != index dimension {_dimensions}");

        if (_embeddings.Length == 0)
            return Result.Success<IReadOnlyList<(string, float)>>(Array.Empty<(string, float)>());

        var effectiveK = Math.Min(topK, _embeddings.Length);

        // Normalize query for cosine similarity
        var normalizedQuery = VectorMath.L2Normalize(queryEmbedding);

        // Compute dot products (= cosine similarity since embeddings are pre-normalized)
        var scores = new (string Id, float Score)[_embeddings.Length];
        for (int i = 0; i < _embeddings.Length; i++)
            scores[i] = (_ids[i], DotProduct(normalizedQuery, _embeddings[i]));

        // Partial sort: get top-K by descending score
        Array.Sort(scores, (a, b) => b.Score.CompareTo(a.Score));

        var results = new (string Id, float Score)[effectiveK];
        Array.Copy(scores, results, effectiveK);
        return Result.Success<IReadOnlyList<(string, float)>>(results);
    }

    private static float DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        return TensorPrimitives.Dot(a, b);
    }


    private static string[] LoadIds(string idsPath)
    {
        return File.ReadAllLines(idsPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
    }

    private static float[][] LoadEmbeddings(string embeddingsPath, int count, int dimensions)
    {
        var fileBytes = File.ReadAllBytes(embeddingsPath);
        var expectedSize = count * dimensions * sizeof(float);
        if (fileBytes.Length != expectedSize)
            throw new InvalidDataException(
                $"Embeddings file size {fileBytes.Length} != expected {expectedSize} " +
                $"({count} vectors x {dimensions} dims x 4 bytes)");

        var floats = MemoryMarshal.Cast<byte, float>(fileBytes.AsSpan());
        var embeddings = new float[count][];
        for (int i = 0; i < count; i++)
        {
            embeddings[i] = new float[dimensions];
            floats.Slice(i * dimensions, dimensions).CopyTo(embeddings[i]);
        }
        return embeddings;
    }
}

