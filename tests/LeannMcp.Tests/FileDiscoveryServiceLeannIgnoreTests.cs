using LeannMcp.Models;
using LeannMcp.Services.Chunking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeannMcp.Tests;

/// <summary>
/// Ignore sources beyond <c>.gitignore</c>: in-tree <c>.leannignore</c> files, and a
/// workspace-wide ignore file that lives outside the repository being indexed so a
/// multi-repo workspace can exclude noise once rather than per repository.
/// </summary>
public sealed class FileDiscoveryServiceLeannIgnoreTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;

    public FileDiscoveryServiceLeannIgnoreTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "leann-ws-" + Guid.NewGuid().ToString("N")[..8]);
        _root = Path.Combine(_workspace, "repo");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, recursive: true);
    }

    [Fact]
    public void DiscoverFiles_LeannIgnoreInRepoRoot_IsHonoured()
    {
        File.WriteAllText(Path.Combine(_root, ".leannignore"), "skipped.txt\n");
        File.WriteAllText(Path.Combine(_root, "skipped.txt"), "skip");
        File.WriteAllText(Path.Combine(_root, "kept.txt"), "keep");

        var docs = Discover();

        Assert.DoesNotContain(docs, d => d.FilePath == "skipped.txt");
        Assert.Contains(docs, d => d.FilePath == "kept.txt");
    }

    [Fact]
    public void DiscoverFiles_LeannIgnoreInSubdirectory_IsScopedToThatSubtree()
    {
        var src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(_root, "other")).FullName;
        File.WriteAllText(Path.Combine(src, ".leannignore"), "notes.txt\n");
        File.WriteAllText(Path.Combine(src, "notes.txt"), "skip");
        File.WriteAllText(Path.Combine(other, "notes.txt"), "keep");

        var docs = Discover();

        Assert.DoesNotContain(docs, d => d.FilePath == "src/notes.txt");
        Assert.Contains(docs, d => d.FilePath == "other/notes.txt");
    }

    [Fact]
    public void DiscoverFiles_WorkspaceIgnoreFile_AppliesToRepoRelativePaths()
    {
        var globalIgnore = Path.Combine(_workspace, ".leannignore");
        File.WriteAllText(globalIgnore, "# workspace rules\n**/obj/**\n**/*.generated.cs\n");

        var obj = Directory.CreateDirectory(Path.Combine(_root, "src", "obj")).FullName;
        File.WriteAllText(Path.Combine(obj, "project.assets.txt"), "build output");
        File.WriteAllText(Path.Combine(_root, "Model.generated.cs"), "generated");
        File.WriteAllText(Path.Combine(_root, "Model.cs"), "real code");

        var docs = Discover(new ChunkingOptions { GlobalIgnoreFile = globalIgnore });

        Assert.DoesNotContain(docs, d => d.FilePath.Contains("obj/"));
        Assert.DoesNotContain(docs, d => d.FilePath == "Model.generated.cs");
        Assert.Contains(docs, d => d.FilePath == "Model.cs");
    }

    [Fact]
    public void DiscoverFiles_MissingWorkspaceIgnoreFile_IsIgnoredSilently()
    {
        File.WriteAllText(Path.Combine(_root, "Model.cs"), "real code");

        var docs = Discover(new ChunkingOptions
        {
            GlobalIgnoreFile = Path.Combine(_workspace, "does-not-exist.leannignore"),
        });

        Assert.Contains(docs, d => d.FilePath == "Model.cs");
    }

    [Fact]
    public void DiscoverFiles_VisualStudioGitignore_ExcludesBuildOutput()
    {
        // The exact pattern shipped in the stock Visual Studio .gitignore.
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "[Bb]in/\n[Oo]bj/\n");

        var obj = Directory.CreateDirectory(Path.Combine(_root, "src", "obj")).FullName;
        File.WriteAllText(Path.Combine(obj, "generated.txt"), "build output");
        File.WriteAllText(Path.Combine(_root, "src", "Real.cs"), "real code");

        var docs = Discover();

        Assert.DoesNotContain(docs, d => d.FilePath.Contains("obj/"));
        Assert.Contains(docs, d => d.FilePath == "src/Real.cs");
    }

    private IReadOnlyList<SourceDocument> Discover(ChunkingOptions? options = null)
    {
        var discovery = new FileDiscoveryService(
            NullLogger<FileDiscoveryService>.Instance,
            new IDocumentReader[] { new PlainTextReader() });
        var result = discovery.DiscoverFiles(_root, options ?? new ChunkingOptions());
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : "");
        return result.Value;
    }
}
