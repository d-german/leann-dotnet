using CSharpFunctionalExtensions;
using LeannMcp.Models;
using System.Text.Json;

namespace LeannMcp.Services;

/// <summary>
/// Manifest-integrity check for a loaded index. After v2.4.0, embedding-model selection
/// is per-index (not global), so this check no longer compares against an active descriptor.
/// Its sole remaining job is to detect a corrupt manifest where the recorded dimensions
/// disagree with the descriptor's dimensions (e.g. interrupted rebuild).
/// </summary>
public static class IndexCompatibility
{
    public static Result EnsureManifestIntegrity(
        IndexMetadata meta,
        EmbeddingModelDescriptor descriptor,
        string indexPath,
        bool validateEmbeddingsMeta = true)
    {
        if (meta.Dimensions != descriptor.Dimensions)
        {
            return Result.Failure(
                $"Index at {indexPath} reports model '{descriptor.Id}' with dim={meta.Dimensions}, " +
                $"but ModelRegistry says that model has dim={descriptor.Dimensions}. " +
                $"The index file is likely corrupt; rebuild with: leann-dotnet --rebuild --index-name {Path.GetFileName(indexPath)}");
        }

        if (validateEmbeddingsMeta)
        {
            var embeddingsMetaResult = EnsureEmbeddingsMetaIntegrity(descriptor, indexPath);
            if (embeddingsMetaResult.IsFailure)
                return embeddingsMetaResult;
        }

        return Result.Success();
    }

    private static Result EnsureEmbeddingsMetaIntegrity(
        EmbeddingModelDescriptor descriptor,
        string indexPath)
    {
        var embeddingsMetaPath = Path.Combine(indexPath, "documents.embeddings.meta.json");
        if (!File.Exists(embeddingsMetaPath))
            return Result.Success();

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(embeddingsMetaPath));
            if (!doc.RootElement.TryGetProperty("model_id", out var modelIdElement))
                return Result.Success();

            var modelId = modelIdElement.GetString();
            if (string.IsNullOrWhiteSpace(modelId) ||
                string.Equals(modelId, descriptor.Id, StringComparison.OrdinalIgnoreCase))
                return Result.Success();

            return Result.Failure(
                $"Index at {indexPath} manifest reports model '{descriptor.Id}', " +
                $"but embeddings metadata reports '{modelId}'. " +
                "The embeddings file is stale or was built with the wrong model; rebuild with --force.");
        }
        catch (Exception ex)
        {
            return Result.Failure(
                $"Failed to read embeddings metadata at {embeddingsMetaPath}: {ex.Message}");
        }
    }
}
