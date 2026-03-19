using CSharpFunctionalExtensions;
using LeannMcp.Models;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Writes passage data and metadata files compatible with the IndexBuilder.
/// </summary>
public interface IPassageWriter
{
    Result WritePassages(
        string indexDir,
        string indexName,
        IReadOnlyList<PassageData> passages,
        IReadOnlyList<string> syncRoots);
}
