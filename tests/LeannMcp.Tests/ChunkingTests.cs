using System.Linq;
using LeannMcp.Models;
using LeannMcp.Services.Chunking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeannMcp.Tests;

public class ChunkingTests
{
    private static ChunkingOptions DefaultOptions => new()
    {
        ChunkSize = 256,
        ChunkOverlap = 128,
        CodeChunkSize = 512,
        CodeChunkOverlap = 64,
        UseAst = true,
    };

    // ---- RoslynChunker ----

    [Fact]
    public void RoslynChunker_TwoMethodsClass_EmitsTwoChunks()
    {
        const string source = """
            namespace Demo;
            public class Foo
            {
                public int Add(int a, int b) { return a + b; }
                public int Sub(int a, int b) { return a - b; }
            }
            """;
        var chunker = new RoslynChunker();
        var chunks = chunker.Chunk(source, DefaultOptions);
        Assert.True(chunks.Count >= 2, $"expected >=2 member chunks, got {chunks.Count}");
        Assert.Contains(chunks, c => c.Contains("Add"));
        Assert.Contains(chunks, c => c.Contains("Sub"));
    }

    [Fact]
    public void RoslynChunker_HandlesMalformedCode_DoesNotThrow()
    {
        const string broken = "namespace X { public class Y { public void M() { if (true) { } "; // missing braces
        var chunker = new RoslynChunker();
        var ex = Record.Exception(() => chunker.Chunk(broken, DefaultOptions));
        Assert.Null(ex);
    }

    [Fact]
    public void RoslynChunker_CanHandle_OnlyCsharp()
    {
        var chunker = new RoslynChunker();
        Assert.True(chunker.CanHandle("csharp"));
        Assert.False(chunker.CanHandle("typescript"));
        Assert.False(chunker.CanHandle(null));
    }

    // ---- BraceBalancedChunker ----

    [Fact]
    public void BraceBalancedChunker_TwoFunctions_EmitsTwoChunks()
    {
        const string source = """
            function add(a, b) {
                return a + b;
            }

            function sub(a, b) {
                return a - b;
            }
            """;
        var chunker = new BraceBalancedChunker();
        var chunks = chunker.Chunk(source, DefaultOptions);
        Assert.True(chunks.Count >= 2, $"expected >=2 chunks, got {chunks.Count}");
    }

    [Fact]
    public void BraceBalancedChunker_StringContainingBraces_NotConfused()
    {
        const string source = """
            function a() {
                var x = "}{}{";
                var y = '}}}}';
                return x + y;
            }
            function b() { return 1; }
            """;
        var chunker = new BraceBalancedChunker();
        var chunks = chunker.Chunk(source, DefaultOptions);
        Assert.True(chunks.Count >= 2);
        Assert.Contains(chunks, c => c.Contains("function a"));
        Assert.Contains(chunks, c => c.Contains("function b"));
    }

    [Fact]
    public void BraceBalancedChunker_TemplateLiteralWithInterpolation_HandlesCorrectly()
    {
        const string source = "function a() {\n    return `hello ${name + '}'} world`;\n}\nfunction b() { return 2; }\n";
        var chunker = new BraceBalancedChunker();
        var chunks = chunker.Chunk(source, DefaultOptions);
        Assert.True(chunks.Count >= 2);
        Assert.Contains(chunks, c => c.Contains("function a"));
        Assert.Contains(chunks, c => c.Contains("function b"));
    }

    [Fact]
    public void BraceBalancedChunker_CanHandle_CFamily()
    {
        var chunker = new BraceBalancedChunker();
        Assert.True(chunker.CanHandle("typescript"));
        Assert.True(chunker.CanHandle("javascript"));
        Assert.True(chunker.CanHandle("java"));
        Assert.False(chunker.CanHandle("csharp"));
        Assert.False(chunker.CanHandle("markdown"));
    }

    // ---- ChunkQualityFilter ----

    [Fact]
    public void QualityFilter_BraceTailChunk_Dropped()
    {
        Assert.False(ChunkQualityFilter.IsAcceptable("}\n}\n"));
        Assert.False(ChunkQualityFilter.IsAcceptable("    }"));
    }

    [Fact]
    public void QualityFilter_Base64Chunk_Dropped()
    {
        var b64 = new string('A', 500);
        Assert.False(ChunkQualityFilter.IsAcceptable(b64));
    }

    [Fact]
    public void QualityFilter_NormalMethodBody_Kept()
    {
        const string body = """
            public int Add(int a, int b)
            {
                return a + b;
            }
            """;
        Assert.True(ChunkQualityFilter.IsAcceptable(body));
    }

    [Fact]
    public void QualityFilter_Filter_RemovesBadKeepsGood()
    {
        var input = new[] { "}", "public void M() { return; }", new string('=', 400), "real chunk with words" };
        var filtered = ChunkQualityFilter.Filter(input);
        Assert.Equal(2, filtered.Count);
    }
}
