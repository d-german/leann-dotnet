using System.IO;
using LeannMcp.Models;
using LeannMcp.Services.Chunking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeannMcp.Tests;

/// <summary>
/// Integration tests for <see cref="PdfChunkingPipeline"/>. Drives the full
/// PDF→passages chain (read structured + read layout + strip header/footer +
/// detect headings + chunk per page + emit PassageData) against in-memory
/// PDFs so the metadata invariants the user cares about (page_start /
/// page_end / heading) can be verified end-to-end.
/// </summary>
public class PdfChunkingPipelineTests
{
    private static PdfChunkingPipeline CreatePipeline()
    {
        var reader = new PdfDocumentReader(NullLogger<PdfDocumentReader>.Instance);
        return new PdfChunkingPipeline(
            structuredReader: reader,
            layoutReader: reader,
            textChunker: new TextChunker(),
            logger: NullLogger<PdfChunkingPipeline>.Instance);
    }

    private static SourceDocument MakeSourceDoc(string filePath) => new()
    {
        Content = string.Empty, // pipeline ignores Content; reads from FilePath
        FilePath = filePath,
        FileName = Path.GetFileName(filePath),
        SourceType = "pdf",
    };

    [Fact]
    public void Chunk_AssignsPageMetadata_FromMultiPagePdf()
    {
        var bytes = PdfFixtureBuilder.BuildTwoPagePdf(
            "ALPHA_TOKEN page-one body content.",
            "BETA_TOKEN page-two body content.");
        var path = PdfFixtureBuilder.WriteTempPdf(bytes);
        try
        {
            var doc = MakeSourceDoc(path);
            var result = CreatePipeline().Chunk(doc, new ChunkingOptions(), firstPassageId: 0);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "");
            var passages = result.Value;
            Assert.NotEmpty(passages);

            foreach (var p in passages)
            {
                Assert.True(p.Metadata!.TryGetValue("page_start", out var ps));
                Assert.True(p.Metadata!.TryGetValue("page_end", out var pe));
                Assert.Equal(ps.GetInt32(), pe.GetInt32());
                Assert.Equal("pdf", p.Metadata!["source_type"].GetString());
                Assert.DoesNotContain("--- Page", p.Text);
            }

            Assert.Contains(passages, p => p.Metadata!["page_start"].GetInt32() == 1);
            Assert.Contains(passages, p => p.Metadata!["page_start"].GetInt32() == 2);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Chunk_PromotesHeadingToMetadata_OnHeadedPage()
    {
        var bytes = PdfFixtureBuilder.BuildPdfWithHeading(
            "Section Alpha",
            "First body sentence.",
            "Second body sentence.",
            "Third body sentence.",
            "Fourth body sentence.");
        var path = PdfFixtureBuilder.WriteTempPdf(bytes);
        try
        {
            var doc = MakeSourceDoc(path);
            var result = CreatePipeline().Chunk(doc, new ChunkingOptions(), firstPassageId: 0);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "");
            var passages = result.Value;
            Assert.NotEmpty(passages);

            var page1 = passages.First(p => p.Metadata!["page_start"].GetInt32() == 1);
            Assert.True(page1.Metadata!.TryGetValue("heading", out var h));
            Assert.Equal("Section Alpha", h.GetString());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Chunk_ChunkText_DoesNotCrossPageBoundaries()
    {
        var bytes = PdfFixtureBuilder.BuildTwoPagePdf("PAGE_ONE_UNIQUE_TOKEN", "PAGE_TWO_UNIQUE_TOKEN");
        var path = PdfFixtureBuilder.WriteTempPdf(bytes);
        try
        {
            var doc = MakeSourceDoc(path);
            var result = CreatePipeline().Chunk(doc, new ChunkingOptions(), firstPassageId: 0);

            Assert.True(result.IsSuccess);
            foreach (var p in result.Value)
            {
                var hasOne = p.Text.Contains("PAGE_ONE_UNIQUE_TOKEN");
                var hasTwo = p.Text.Contains("PAGE_TWO_UNIQUE_TOKEN");
                Assert.False(hasOne && hasTwo, "Chunk should not span pages.");
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Chunk_PassageIds_StartAtFirstPassageId_AndAreSequential()
    {
        var bytes = PdfFixtureBuilder.BuildTwoPagePdf("A", "B");
        var path = PdfFixtureBuilder.WriteTempPdf(bytes);
        try
        {
            var doc = MakeSourceDoc(path);
            var result = CreatePipeline().Chunk(doc, new ChunkingOptions(), firstPassageId: 100);

            Assert.True(result.IsSuccess);
            var passages = result.Value;
            for (var i = 0; i < passages.Count; i++)
                Assert.Equal((100 + i).ToString(), passages[i].Id);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Chunk_MissingFile_ReturnsFailure()
    {
        var doc = MakeSourceDoc(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.pdf"));
        var result = CreatePipeline().Chunk(doc, new ChunkingOptions(), firstPassageId: 0);
        Assert.True(result.IsFailure);
    }
}
