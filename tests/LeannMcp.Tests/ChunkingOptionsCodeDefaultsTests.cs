using LeannMcp.Models;
using LeannMcp.Services.Chunking;
using Xunit;

namespace LeannMcp.Tests;

public class ChunkingOptionsCodeDefaultsTests
{
    [Fact]
    public void CodeChunkDefaults_FitTheEmbeddingWindowWithTwelvePointFivePercentOverlap()
    {
        var options = new ChunkingOptions();

        Assert.Equal(1536, options.CodeChunkSize);
        Assert.Equal(192, options.CodeChunkOverlap);
        Assert.Equal(ChunkingOptions.DefaultCodeChunkSize, options.CodeChunkSize);
        Assert.Equal(ChunkingOptions.DefaultCodeChunkOverlap, options.CodeChunkOverlap);
    }

    [Fact]
    public void RoslynChunker_WithDefaultOptions_PacksSmallMembersAndStaysWithinTheDefaultSize()
    {
        var members = string.Join("\n", Enumerable.Range(0, 60).Select(i => $"    public int Field{i};"));
        var source = $"namespace Demo;\npublic class Wide\n{{\n{members}\n}}\n";

        var chunks = new RoslynChunker().Chunk(source, new ChunkingOptions());

        Assert.InRange(chunks.Count, 1, 3);
        Assert.All(chunks, c => Assert.True(c.Length <= ChunkingOptions.DefaultCodeChunkSize));
    }
}
