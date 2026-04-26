using System.Text.Json;
using CSharpFunctionalExtensions;
using LeannMcp.Models;
using Microsoft.Extensions.Logging;

namespace LeannMcp.Services.Chunking;

/// <summary>
/// Default <see cref="IPdfChunkingPipeline"/> that orchestrates
/// <see cref="IStructuredDocumentReader"/>, <see cref="IPdfLayoutReader"/>,
/// <see cref="HeaderFooterStripper"/>, <see cref="HeadingDetector"/>, and
/// <see cref="ITextChunker"/> to convert a single PDF into metadata-rich
/// <see cref="PassageData"/>.
/// <para/>
/// Pipeline (Railway-Oriented):
/// <code>
/// ReadStructured(file)                  ──► IReadOnlyList&lt;PageSegment&gt;
///   .Map(HeaderFooterStripper.Strip)    ──► IReadOnlyList&lt;PageSegment&gt; (boilerplate removed)
///   .Bind(stripped => ReadLayout(file)
///     .Map(HeadingDetector.Detect))     ──► IReadOnlyList&lt;Heading&gt;
///   .Map(BuildPassages)                 ──► IReadOnlyList&lt;PassageData&gt;
/// </code>
/// Each page is chunked independently — chunks never cross page boundaries
/// — so the page_start / page_end metadata is always exact and citation
/// stays precise. Resolves user-reported defects D2 (oversized overlap),
/// D3 (footer leakage), D4 (no heading metadata), and D5 (page markers in
/// body text).
/// </summary>
public sealed class PdfChunkingPipeline(
    IStructuredDocumentReader structuredReader,
    IPdfLayoutReader layoutReader,
    ITextChunker textChunker,
    ILogger<PdfChunkingPipeline> logger) : IPdfChunkingPipeline
{
    public Result<IReadOnlyList<PassageData>> Chunk(
        SourceDocument document,
        ChunkingOptions options,
        int firstPassageId)
    {
        var path = document.AbsolutePath ?? document.FilePath;
        return structuredReader.ReadStructured(path)
            .Map(pages => HeaderFooterStripper.Strip(pages, options.PdfBoilerplateRepeatRatio))
            .Bind(stripped => DetectHeadings(path, options)
                .Map(headings => (Pages: stripped, Headings: headings)))
            .Map(state => BuildPassages(document, options, state.Pages, state.Headings, firstPassageId))
            .TapError(err => logger.LogWarning("PDF chunk skipped: {Path}: {Err}", document.FilePath, err));
    }

    private Result<IReadOnlyList<Heading>> DetectHeadings(string filePath, ChunkingOptions options)
    {
        return layoutReader.ReadLayout(filePath)
            .Map(layout => HeadingDetector.Detect(layout, options.PdfMinHeadingFontRatio));
    }

    private IReadOnlyList<PassageData> BuildPassages(
        SourceDocument document,
        ChunkingOptions options,
        IReadOnlyList<PageSegment> pages,
        IReadOnlyList<Heading> headings,
        int firstPassageId)
    {
        var headingsByPage = headings
            .GroupBy(h => h.PageNumber)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Heading>)g.ToList());

        var passages = new List<PassageData>();
        var nextId = firstPassageId;

        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.Text)) continue;

            var chunks = textChunker.ChunkText(
                page.Text, options.PdfChunkSize, options.PdfChunkOverlap, SplitMode.Sentence);

            var pageHeading = headingsByPage.TryGetValue(page.Number, out var hs) && hs.Count > 0
                ? hs[0].Text
                : null;

            foreach (var chunk in chunks)
            {
                var trimmed = chunk.Trim();
                if (trimmed.Length == 0) continue;
                passages.Add(CreatePassage(nextId++, trimmed, document, page.Number, pageHeading));
            }
        }
        return passages;
    }

    private static PassageData CreatePassage(
        int id, string text, SourceDocument doc, int pageNumber, string? heading)
    {
        var metadata = new Dictionary<string, JsonElement>();
        AddMetadata(metadata, "file_path", doc.FilePath);
        AddMetadata(metadata, "file_name", doc.FileName);
        AddMetadata(metadata, "source", doc.FilePath);
        AddMetadata(metadata, "source_type", doc.SourceType);
        AddMetadata(metadata, "page_start", pageNumber);
        AddMetadata(metadata, "page_end", pageNumber);
        if (heading is not null)
            AddMetadata(metadata, "heading", heading);

        if (doc.CreationDate.HasValue)
            AddMetadata(metadata, "creation_date", doc.CreationDate.Value.ToString("yyyy-MM-dd"));
        if (doc.LastModifiedDate.HasValue)
            AddMetadata(metadata, "last_modified_date", doc.LastModifiedDate.Value.ToString("yyyy-MM-dd"));

        return new PassageData(id.ToString(), text, metadata);
    }

    private static void AddMetadata(Dictionary<string, JsonElement> dict, string key, string value) =>
        dict[key] = JsonSerializer.SerializeToElement(value);

    private static void AddMetadata(Dictionary<string, JsonElement> dict, string key, int value) =>
        dict[key] = JsonSerializer.SerializeToElement(value);
}
