using CSharpFunctionalExtensions;
using LeannMcp.Models;

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
        string indexPath)
    {
        if (meta.Dimensions != descriptor.Dimensions)
        {
            return Result.Failure(
                $"Index at {indexPath} reports model '{descriptor.Id}' with dim={meta.Dimensions}, " +
                $"but ModelRegistry says that model has dim={descriptor.Dimensions}. " +
                $"The index file is likely corrupt; rebuild with: leann-dotnet --rebuild --index-name {Path.GetFileName(indexPath)}");
        }

        return Result.Success();
    }
}
