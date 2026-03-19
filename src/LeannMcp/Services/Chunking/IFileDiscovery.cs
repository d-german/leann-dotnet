using CSharpFunctionalExtensions;
using LeannMcp.Models;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Discovers files in a directory tree, respecting .gitignore rules.
/// </summary>
public interface IFileDiscovery
{
    Result<IReadOnlyList<SourceDocument>> DiscoverFiles(string rootPath, ChunkingOptions options);
}
