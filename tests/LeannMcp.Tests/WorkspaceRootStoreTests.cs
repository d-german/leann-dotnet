using System.Collections.Immutable;
using LeannMcp.Services.Workspace;
using Xunit;

namespace LeannMcp.Tests;

public class WorkspaceRootStoreTests
{
    [Fact]
    public void DefaultSnapshot_IsEmpty()
    {
        var store = new WorkspaceRootStore();
        Assert.True(store.Snapshot.ListRoots.IsEmpty);
        Assert.True(store.Snapshot.InitializeRoots.IsEmpty);
    }

    [Fact]
    public void SetListRoots_UpdatesSnapshot_WithoutTouchingInitializeRoots()
    {
        var store = new WorkspaceRootStore();
        store.SetInitializeRoots(ImmutableArray.Create("C:\\init"));
        store.SetListRoots(ImmutableArray.Create("C:\\list"));

        Assert.Equal(new[] { "C:\\list" }, store.Snapshot.ListRoots);
        Assert.Equal(new[] { "C:\\init" }, store.Snapshot.InitializeRoots);
    }

    [Fact]
    public void Clear_ResetsToEmpty()
    {
        var store = new WorkspaceRootStore();
        store.SetListRoots(ImmutableArray.Create("C:\\x"));
        store.Clear();
        Assert.True(store.Snapshot.ListRoots.IsEmpty);
    }

    [Fact]
    public void FromUriStrings_FiltersNonFile_AndTrims()
    {
        var input = new[] { "  ", null, "http://example", "file:///C:/foo/bar", "  file:///C:/baz  " };
        var result = WorkspaceRootConverter.FromUriStrings(input);
        Assert.Equal(2, result.Length);
        Assert.Contains(result, p => p.Replace('\\', '/').EndsWith("foo/bar", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result, p => p.Replace('\\', '/').EndsWith("baz", StringComparison.OrdinalIgnoreCase));
    }
}
