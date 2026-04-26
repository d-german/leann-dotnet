using LeannMcp.Models;
using LeannMcp.Services.Chunking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeannMcp.Tests;

public sealed class FileDiscoveryServiceGitIgnoreTests : IDisposable
{
    private readonly string _root;

    public FileDiscoveryServiceGitIgnoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "leann-ignore-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void DiscoverFiles_SubdirectoryGitignore_IsScopedToThatSubtree()
    {
        var src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(_root, "other")).FullName;
        File.WriteAllText(Path.Combine(src, ".gitignore"), "ignored.txt\n");
        File.WriteAllText(Path.Combine(src, "ignored.txt"), "skip");
        File.WriteAllText(Path.Combine(other, "ignored.txt"), "keep");

        var docs = Discover();

        Assert.DoesNotContain(docs, d => d.FilePath == "src/ignored.txt");
        Assert.Contains(docs, d => d.FilePath == "other/ignored.txt");
    }

    [Fact]
    public void DiscoverFiles_DeepGitignore_IsLoadedRecursively()
    {
        var generated = Directory.CreateDirectory(Path.Combine(_root, "src", "generated")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(_root, "other")).FullName;
        File.WriteAllText(Path.Combine(generated, ".gitignore"), "*.cs\n");
        File.WriteAllText(Path.Combine(generated, "Skip.cs"), "class Skip {}");
        File.WriteAllText(Path.Combine(other, "Keep.cs"), "class Keep {}");

        var docs = Discover();

        Assert.DoesNotContain(docs, d => d.FilePath == "src/generated/Skip.cs");
        Assert.Contains(docs, d => d.FilePath == "other/Keep.cs");
    }

    private IReadOnlyList<SourceDocument> Discover()
    {
        var discovery = new FileDiscoveryService(
            NullLogger<FileDiscoveryService>.Instance,
            new IDocumentReader[] { new PlainTextReader() });
        var result = discovery.DiscoverFiles(_root, new ChunkingOptions());
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "");
        return result.Value;
    }
}
