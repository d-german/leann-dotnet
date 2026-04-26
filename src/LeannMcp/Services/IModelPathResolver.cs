using LeannMcp.Models;

namespace LeannMcp.Services;

/// <summary>
/// Resolves the on-disk directory containing a model's ONNX file and tokenizer assets.
/// Mirrors the legacy GetModelDir helper in Program.cs while exposing it as a DI-friendly abstraction.
/// </summary>
public interface IModelPathResolver
{
    string GetModelDirectory(EmbeddingModelDescriptor descriptor);
}

public sealed class DefaultModelPathResolver : IModelPathResolver
{
    public string GetModelDirectory(EmbeddingModelDescriptor descriptor)
    {
        var legacy = Environment.GetEnvironmentVariable("LEANN_MODEL_DIR");
        if (!string.IsNullOrWhiteSpace(legacy)) return legacy;

        var safeId = descriptor.Id.Replace('/', '-').Replace('\\', '-');
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".leann", "models", safeId);
    }
}
