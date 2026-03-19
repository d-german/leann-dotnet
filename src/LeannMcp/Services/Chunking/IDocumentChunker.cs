using CSharpFunctionalExtensions;
using LeannMcp.Models;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Orchestrates chunking of source documents into passages.
/// </summary>
public interface IDocumentChunker
{
    Result<IReadOnlyList<PassageData>> ChunkDocuments(
        IReadOnlyList<SourceDocument> documents, ChunkingOptions options);
}
