using System.Text.Json;
using CSharpFunctionalExtensions;
using LeannMcp.Models;
using Microsoft.Extensions.Logging;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Orchestrates document chunking. Routes each document to a registered
/// <see cref="IChunkStrategy"/> based on its language (Roslyn for C#,
/// brace-balanced for C-family). Falls back to <see cref="ITextChunker"/>'s
/// line-based splitter when no strategy claims the language or when AST is
/// disabled. Applies <see cref="ChunkQualityFilter"/> as a post-step on every
/// document's chunks to drop low-signal content (lone braces, base64 blobs,
/// generated boilerplate).
/// </summary>
public sealed class DocumentChunker : IDocumentChunker
{
    private readonly ITextChunker _textChunker;
    private readonly IReadOnlyList<IChunkStrategy> _strategies;
    private readonly ILogger<DocumentChunker> _logger;

    public DocumentChunker(
        ITextChunker textChunker,
        IEnumerable<IChunkStrategy> strategies,
        ILogger<DocumentChunker> logger)
    {
        _textChunker = textChunker;
        _strategies = strategies.ToList();
        _logger = logger;
    }

    public Result<IReadOnlyList<PassageData>> ChunkDocuments(
        IReadOnlyList<SourceDocument> documents, ChunkingOptions options)
    {
        if (documents.Count == 0)
            return Result.Failure<IReadOnlyList<PassageData>>("No documents to chunk");

        var passages = new List<PassageData>();
        var globalId = 0;
        var droppedByFilter = 0;

        for (var i = 0; i < documents.Count; i++)
        {
            var doc = documents[i];
            var rawChunks = ChunkSingleDocument(doc, options);
            var filteredChunks = ChunkQualityFilter.Filter(rawChunks);
            droppedByFilter += rawChunks.Count - filteredChunks.Count;

            foreach (var chunkText in filteredChunks)
                passages.Add(CreatePassage(globalId++, chunkText, doc));

            if ((i + 1) % 50 == 0)
                _logger.LogInformation("  Chunked {Done}/{Total} documents — {Passages} passages so far",
                    i + 1, documents.Count, passages.Count);
        }

        _logger.LogInformation(
            "Chunking complete: {Docs} documents → {Passages} passages ({Dropped} dropped by quality filter)",
            documents.Count, passages.Count, droppedByFilter);

        return Result.Success<IReadOnlyList<PassageData>>(passages);
    }

    private IReadOnlyList<string> ChunkSingleDocument(SourceDocument doc, ChunkingOptions options)
    {
        var strategy = SelectStrategy(doc, options);
        if (strategy is not null)
            return strategy.Chunk(doc.Content, options);

        // Fallback: use the legacy line-based / sentence-based text chunker.
        return doc.IsCode
            ? _textChunker.ChunkText(doc.Content, options.CodeChunkSize, options.CodeChunkOverlap, SplitMode.CodeLine)
            : _textChunker.ChunkText(doc.Content, options.ChunkSize, options.ChunkOverlap, SplitMode.Sentence);
    }

    private IChunkStrategy? SelectStrategy(SourceDocument doc, ChunkingOptions options)
    {
        if (!options.UseAst || !doc.IsCode || doc.Language is null) return null;
        return _strategies.FirstOrDefault(s => s.CanHandle(doc.Language));
    }

    private static PassageData CreatePassage(int id, string text, SourceDocument doc)
    {
        var metadata = new Dictionary<string, JsonElement>();

        AddMetadata(metadata, "file_path", doc.FilePath);
        AddMetadata(metadata, "file_name", doc.FileName);
        AddMetadata(metadata, "source", doc.FilePath);
        AddMetadata(metadata, "source_type", doc.SourceType);

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
