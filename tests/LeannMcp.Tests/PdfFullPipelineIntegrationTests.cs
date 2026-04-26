using System.IO;
using LeannMcp.Models;
using LeannMcp.Services.Chunking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeannMcp.Tests;

/// <summary>
/// Kitchen-sink end-to-end test: builds a multi-page PDF with a repeating
/// footer + a single oversized heading on page 1 + unique body content per
/// page, runs the full PdfChunkingPipeline, and asserts that ALL of T07
/// (boilerplate stripped), T08 (heading promoted to metadata), and T09
/// (page metadata + no marker leakage + no cross-page chunks) hold
/// simultaneously. The intent is to lock in the quality wins so future
/// refactors don't silently regress any one of them.
/// </summary>
public class PdfFullPipelineIntegrationTests
{
    private const string Footer = "Hyland Clinician Window © Hyland Software, Inc. 2024";
    private const string Heading = "Section 1: Overview";

    private static PdfChunkingPipeline CreatePipeline()
    {
        var reader = new PdfDocumentReader(NullLogger<PdfDocumentReader>.Instance);
        return new PdfChunkingPipeline(
            structuredReader: reader,
            layoutReader: reader,
            textChunker: new TextChunker(),
            logger: NullLogger<PdfChunkingPipeline>.Instance);
    }

    [Fact]
    public void EndToEnd_StripsFooter_PromotesHeading_AssignsPagesPerChunk()
    {
        var perPage = new IReadOnlyList<string>[]
        {
            new[] { "Body sentence 1A.", "Body sentence 1B.", "Body sentence 1C." },
            new[] { "Body sentence 2A.", "Body sentence 2B.", "Body sentence 2C." },
            new[] { "Body sentence 3A.", "Body sentence 3B.", "Body sentence 3C." },
        };
        var bytes = PdfFixtureBuilder.BuildPdfWithBoilerplateAndHeading(Heading, Footer, perPage);
        var path = PdfFixtureBuilder.WriteTempPdf(bytes);
        try
        {
            var doc = new SourceDocument
            {
                Content = string.Empty,
                FilePath = path,
                FileName = Path.GetFileName(path),
                SourceType = "pdf",
            };

            var result = CreatePipeline().Chunk(doc, new ChunkingOptions(), firstPassageId: 0);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "");
            var passages = result.Value;
            Assert.NotEmpty(passages);

            // T07: boilerplate footer stripped from every chunk.
            foreach (var p in passages)
                Assert.DoesNotContain("Hyland", p.Text);

            // T09: no page markers leak.
            foreach (var p in passages)
                Assert.DoesNotContain("--- Page", p.Text);

            // T09: every chunk carries page metadata; page_start == page_end.
            foreach (var p in passages)
            {
                Assert.True(p.Metadata!.TryGetValue("page_start", out var ps));
                Assert.True(p.Metadata!.TryGetValue("page_end", out var pe));
                Assert.Equal(ps.GetInt32(), pe.GetInt32());
                Assert.InRange(ps.GetInt32(), 1, 3);
            }

            // T08: page-1 chunks carry the heading; later pages don't.
            var page1Chunks = passages.Where(p => p.Metadata!["page_start"].GetInt32() == 1).ToList();
            Assert.NotEmpty(page1Chunks);
            Assert.All(page1Chunks, p =>
            {
                Assert.True(p.Metadata!.TryGetValue("heading", out var h));
                Assert.Equal(Heading, h.GetString());
            });

            var laterPages = passages.Where(p => p.Metadata!["page_start"].GetInt32() > 1).ToList();
            Assert.NotEmpty(laterPages);
            Assert.All(laterPages, p => Assert.False(p.Metadata!.ContainsKey("heading")));

            // All three pages contributed at least one chunk (no page silently dropped).
            for (var page = 1; page <= 3; page++)
                Assert.Contains(passages, p => p.Metadata!["page_start"].GetInt32() == page);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void EndToEnd_DocumentChunker_DispatchesPdfThroughPipeline()
    {
        // Verifies the routing wire: DocumentChunker.ChunkDocuments must
        // detect SourceType=='pdf' and route to IPdfChunkingPipeline (not
        // through the legacy text chunker).
        var perPage = new IReadOnlyList<string>[]
        {
            new[] { "PIPELINE_TOKEN body line one.", "PIPELINE_TOKEN body line two." },
            new[] { "PIPELINE_TOKEN page two line one.", "PIPELINE_TOKEN page two line two." },
        };
        var bytes = PdfFixtureBuilder.BuildPdfWithBoilerplateAndHeading(
            "Heading X", "Footer Y", perPage);
        var path = PdfFixtureBuilder.WriteTempPdf(bytes);
        try
        {
            var pipeline = CreatePipeline();
            var chunker = new DocumentChunker(
                new TextChunker(),
                Enumerable.Empty<ICodeChunkStrategy>(),
                pipeline,
                NullLogger<DocumentChunker>.Instance);
            var docs = new[]
            {
                new SourceDocument
                {
                    Content = "irrelevant",
                    FilePath = path,
                    FileName = Path.GetFileName(path),
                    SourceType = "pdf",
                },
            };

            var result = chunker.ChunkDocuments(docs, new ChunkingOptions());

            Assert.True(result.IsSuccess);
            var passages = result.Value;
            Assert.NotEmpty(passages);
            Assert.All(passages, p =>
            {
                Assert.True(p.Metadata!.ContainsKey("page_start"));
                Assert.DoesNotContain("Footer Y", p.Text);
            });
        }
        finally { File.Delete(path); }
    }
}
