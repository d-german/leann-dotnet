using CSharpFunctionalExtensions;
using LeannMcp.Models;

namespace LeannMcp.Services;

public static class IndexCompatibility
{
    public static Result EnsureCompatibleModel(
        IndexMetadata meta,
        EmbeddingModelDescriptor active,
        string indexPath)
    {
        var indexModel = meta.EmbeddingModel;
        var indexDim = meta.Dimensions;

        if (string.IsNullOrEmpty(indexModel))
        {
            return Result.Failure(BuildMessage(indexPath, "<unknown>", indexDim, active));
        }

        if (!string.Equals(indexModel, active.Id, StringComparison.Ordinal))
        {
            return Result.Failure(BuildMessage(indexPath, indexModel, indexDim, active));
        }

        if (indexDim != active.Dimensions)
        {
            return Result.Failure(
                $"Index at {indexPath} was built with embedding model '{indexModel}' (dim={indexDim}) " +
                $"but the active model is '{active.Id}' (dim={active.Dimensions}). " +
                "Dimension mismatch indicates a corrupt index. " +
                "Run 'leann-dotnet --rebuild' to rebuild against the active model.");
        }

        return Result.Success();
    }

    private static string BuildMessage(string indexPath, string indexModel, int indexDim, EmbeddingModelDescriptor active) =>
        $"Index at {indexPath} was built with embedding model '{indexModel}' (dim={indexDim}) " +
        $"but the active model is '{active.Id}' (dim={active.Dimensions}). " +
        "Cosine similarity across different embedding spaces is meaningless. " +
        $"Either: (1) set environment variable LEANN_MODEL={indexModel} to query this index with its original model, " +
        "or (2) run 'leann-dotnet --rebuild' to rebuild against the active model.";
}
