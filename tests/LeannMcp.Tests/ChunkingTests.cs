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

    // ---- IndentationChunker ----

    [Fact]
    public void IndentationChunker_TwoFunctions_EmitsTwoChunks()
    {
        const string source = """
            def hello():
                print('hello')

            def world():
                print('world')
            """;
        var chunker = new IndentationChunker();
        var chunks = chunker.Chunk(source, DefaultOptions);
        Assert.True(chunks.Count >= 2, $"expected >=2 chunks, got {chunks.Count}");
    }

    [Fact]
    public void IndentationChunker_ClassWithMethods_EmitsOneChunk()
    {
        const string source = """
            class MyClass:
                def method1(self):
                    pass
                def method2(self):
                    pass
            """;
        var chunker = new IndentationChunker();
        var chunks = chunker.Chunk(source, DefaultOptions);
        Assert.Single(chunks);
    }

    [Fact]
    public void IndentationChunker_DecoratorGroupedWithFunction()
    {
        const string source = """
            def first():
                pass

            @my_decorator
            def second():
                pass
            """;
        var chunker = new IndentationChunker();
        var chunks = chunker.Chunk(source, DefaultOptions);
        Assert.DoesNotContain(chunks, c => c.Contains("@my_decorator") && !c.Contains("def second"));
    }

    [Fact]
    public void IndentationChunker_TripleQuotedString_NoFalseBoundary()
    {
        const string source = """"
            def example():
                doc = """
            This text is at column 0
            but should not split the block
            """
                return doc
            """";
        var chunker = new IndentationChunker();
        var chunks = chunker.Chunk(source, DefaultOptions);
        Assert.Single(chunks);
    }

    [Fact]
    public void IndentationChunker_BlankLinesBetweenFunctions_StillSplits()
    {
        const string source = """
            def first():
                pass



            def second():
                pass
            """;
        var chunker = new IndentationChunker();
        var chunks = chunker.Chunk(source, DefaultOptions);
        Assert.True(chunks.Count >= 2, $"expected >=2 chunks, got {chunks.Count}");
    }

    [Fact]
    public void IndentationChunker_CanHandle_OnlyPython()
    {
        var chunker = new IndentationChunker();
        Assert.True(chunker.CanHandle("python"));
        Assert.True(chunker.CanHandle("Python"));
        Assert.False(chunker.CanHandle("csharp"));
        Assert.False(chunker.CanHandle("javascript"));
        Assert.False(chunker.CanHandle(null));
    }

    [Fact]
    public void IndentationChunker_EmptyInput_ReturnsEmpty()
    {
        var chunker = new IndentationChunker();
        Assert.Empty(chunker.Chunk("", DefaultOptions));
        Assert.Empty(chunker.Chunk("   \n  \n  ", DefaultOptions));
    }

    [Fact]
    public void IndentationChunker_OversizedBlock_SplitsAtDedent()
    {
        var lines = new System.Collections.Generic.List<string> { "def big_function():" };
        for (var i = 0; i < 20; i++)
            lines.Add($"    x_{i} = {i}");
        var source = string.Join('\n', lines);

        var options = new ChunkingOptions
        {
            ChunkSize = 256,
            ChunkOverlap = 128,
            CodeChunkSize = 100,
            CodeChunkOverlap = 64,
            UseAst = true,
        };

        var chunker = new IndentationChunker();
        var chunks = chunker.Chunk(source, options);
        Assert.True(chunks.Count >= 2, $"expected >=2 chunks, got {chunks.Count}");
    }
}
