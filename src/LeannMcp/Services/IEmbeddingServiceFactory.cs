using CSharpFunctionalExtensions;
using LeannMcp.Models;

namespace LeannMcp.Services;

/// <summary>
/// Resolves an <see cref="IEmbeddingService"/> for a given <see cref="EmbeddingModelDescriptor"/>,
/// caching one instance per model id. Enables a single MCP-server process to embed queries
/// using whichever model an index was built with (per-index model selection).
/// </summary>
public interface IEmbeddingServiceFactory
{
    Result<IEmbeddingService> GetOrCreate(EmbeddingModelDescriptor descriptor);
    IReadOnlyCollection<string> LoadedModelIds { get; }
}
