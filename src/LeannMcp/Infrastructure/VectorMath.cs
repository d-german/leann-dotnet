using System.Numerics.Tensors;

namespace LeannMcp.Infrastructure;

/// <summary>
/// Shared vector math utilities used by both FlatVectorIndex (search) and IndexBuilder (build).
/// </summary>
public static class VectorMath
{
    public static float[] L2Normalize(float[] vector)
    {
        var norm = MathF.Sqrt(TensorPrimitives.Dot(vector.AsSpan(), vector.AsSpan()));
        if (norm < 1e-12f) return vector;

        var result = new float[vector.Length];
        var invNorm = 1f / norm;
        for (int i = 0; i < vector.Length; i++)
            result[i] = vector[i] * invNorm;
        return result;
    }
}
