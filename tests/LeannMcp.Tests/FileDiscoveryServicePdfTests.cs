using System.IO;
using System.Linq;
using LeannMcp.Models;
using LeannMcp.Services.Chunking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeannMcp.Tests;

public class FileDiscoveryServicePdfTests
{
    private static FileDiscoveryService CreateDiscovery() =>
        new(
            NullLogger<FileDiscoveryService>.Instance,
            new IDocumentReader[]
            {
                new PdfDocumentReader(NullLogger<PdfDocumentReader>.Instance),
                new PlainTextReader(),
            });

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"leann-disc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void DiscoverFiles_PdfInDirectory_YieldsPdfSourceDocument()
    {
        var dir = CreateTempDir();
        try
        {
            var bytes = PdfFixtureBuilder.BuildTwoPagePdf("ALPHA_TOKEN", "BETA_TOKEN");
            var pdfPath = Path.Combine(dir, "doc.pdf");
            File.WriteAllBytes(pdfPath, bytes);

            var result = CreateDiscovery().DiscoverFiles(dir, new ChunkingOptions());

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "");
            var docs = result.Value;
            Assert.Single(docs);
            var doc = docs[0];
            Assert.Equal("pdf", doc.SourceType);
            Assert.False(doc.IsCode);
            Assert.Contains("ALPHA_TOKEN", doc.Content);
            Assert.Contains("BETA_TOKEN", doc.Content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DiscoverFiles_CorruptPdf_IsSkippedWithoutAbortingDiscovery()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "broken.pdf"), "not a pdf");
            File.WriteAllText(Path.Combine(dir, "notes.md"), "# Hello\n\nWorld");

            var result = CreateDiscovery().DiscoverFiles(dir, new ChunkingOptions());

            Assert.True(result.IsSuccess);
            var docs = result.Value;
            Assert.Single(docs);
            Assert.Equal("notes.md", docs[0].FileName);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DiscoverFiles_PdfFlowsThroughChunker_WithSourceTypeMetadata()
    {
        var dir = CreateTempDir();
        try
        {
            var bytes = PdfFixtureBuilder.BuildTwoPagePdf("GAMMA_TOKEN with some sentence content here.", "DELTA_TOKEN with another sentence on page two.");
            File.WriteAllBytes(Path.Combine(dir, "doc.pdf"), bytes);

            var discovery = CreateDiscovery();
            var docs = discovery.DiscoverFiles(dir, new ChunkingOptions()).Value;

            var chunker = new DocumentChunker(
                new TextChunker(),
                Enumerable.Empty<IChunkStrategy>(),
                NullLogger<DocumentChunker>.Instance);
            var options = new ChunkingOptions { ChunkSize = 128, ChunkOverlap = 32, CodeChunkSize = 256, CodeChunkOverlap = 32, UseAst = false };
            var chunkResult = chunker.ChunkDocuments(docs, options);

            Assert.True(chunkResult.IsSuccess, chunkResult.IsFailure ? chunkResult.Error : "");
            Assert.NotEmpty(chunkResult.Value);
            foreach (var passage in chunkResult.Value)
            {
                Assert.True(passage.Metadata.TryGetValue("source_type", out var st));
                Assert.Equal("pdf", st.GetString()!);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
