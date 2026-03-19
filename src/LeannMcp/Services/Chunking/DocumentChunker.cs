using System.Text.Json;
using CSharpFunctionalExtensions;
using LeannMcp.Models;
using Microsoft.Extensions.Logging;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Orchestrates document chunking: routes code files to line-based splitting and
/// text files to sentence-based splitting, producing PassageData with sequential IDs.
/// </summary>
public sealed class DocumentChunker(ITextChunker textChunker, ILogger<DocumentChunker> logger) : IDocumentChunker
{
    public Result<IReadOnlyList<PassageData>> ChunkDocuments(
        IReadOnlyList<SourceDocument> documents, ChunkingOptions options)
    {
        if (documents.Count == 0)
            return Result.Failure<IReadOnlyList<PassageData>>("No documents to chunk");

        var passages = new List<PassageData>();
        var globalId = 0;

        for (var i = 0; i < documents.Count; i++)
        {
            var doc = documents[i];
            var chunks = ChunkSingleDocument(doc, options);

            foreach (var chunkText in chunks)
            {
                passages.Add(CreatePassage(globalId++, chunkText, doc));
            }

            if ((i + 1) % 50 == 0)
                logger.LogInformation("  Chunked {Done}/{Total} documents — {Passages} passages so far",
                    i + 1, documents.Count, passages.Count);
        }

        logger.LogInformation("Chunking complete: {Docs} documents → {Passages} passages",
            documents.Count, passages.Count);

        return Result.Success<IReadOnlyList<PassageData>>(passages);
    }

    private IReadOnlyList<string> ChunkSingleDocument(SourceDocument doc, ChunkingOptions options)
    {
        if (doc.IsCode)
        {
            return textChunker.ChunkText(
                doc.Content, options.CodeChunkSize, options.CodeChunkOverlap, SplitMode.CodeLine);
        }

        return textChunker.ChunkText(
            doc.Content, options.ChunkSize, options.ChunkOverlap, SplitMode.Sentence);
    }

    private static PassageData CreatePassage(int id, string text, SourceDocument doc)
    {
        var metadata = new Dictionary<string, JsonElement>();

        AddMetadata(metadata, "file_path", doc.FilePath);
        AddMetadata(metadata, "file_name", doc.FileName);
        AddMetadata(metadata, "source", doc.FilePath);

        if (doc.CreationDate.HasValue)
            AddMetadata(metadata, "creation_date", doc.CreationDate.Value.ToString("yyyy-MM-dd"));

        if (doc.LastModifiedDate.HasValue)
            AddMetadata(metadata, "last_modified_date", doc.LastModifiedDate.Value.ToString("yyyy-MM-dd"));

        return new PassageData(id.ToString(), text, metadata);
    }

    private static void AddMetadata(Dictionary<string, JsonElement> dict, string key, string value)
    {
        dict[key] = JsonSerializer.SerializeToElement(value);
    }
}
