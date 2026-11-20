using JGlancer.Core.Model;
using JGlancer.Core.Store;
using static JGlancer.Core.Tests.Store.StoreTestSupport;

namespace JGlancer.Core.Tests.Store;

/// <summary>Hidden subfolders and the smallest files leave the library's views (owner, 0.6.1).</summary>
public sealed class HiddenAndSmallFilesTests
{
    private static async Task<(LibraryStore Store, IReadOnlyList<Folder> Folders)> LibraryAsync()
    {
        var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\lib");
        await store.ScanAsync(root.Id, "", [File("top.jpg"), File("icon.png", size: 300)], ["a", "ab"]);
        await store.ScanAsync(root.Id, "a", [File("in-a.jpg")], ["b"]);
        await store.ScanAsync(root.Id, "a/b", [File("in-b.jpg")]);
        await store.ScanAsync(root.Id, "ab", [File("in-ab.jpg")]);
        return (store, await store.FoldersAsync(root.Id));
    }

    [Fact]
    public async Task AHiddenFolderTakesItsSubtreeAlongButNotItsNeighbors()
    {
        var (store, folders) = await LibraryAsync();
        using var _ = store;
        var a = folders.First(f => f.RelativePath == "a").Id;
        var names = (await store.NamesAsync(new AssetQuery { HiddenFolders = [a] })).Order(StringComparer.Ordinal);
        // "ab" only shares the prefix: it stays.
        Assert.Equal(["icon.png", "in-ab.jpg", "top.jpg"], names);
        Assert.Equal(5, (await store.NamesAsync(new AssetQuery())).Count);
    }

    [Fact]
    public async Task FilesBelowTheSmallestSizeAreLeftOut()
    {
        var (store, _) = await LibraryAsync();
        using var __ = store;
        Assert.DoesNotContain("icon.png", await store.NamesAsync(new AssetQuery { MinSize = 400 }));
        Assert.Contains("icon.png", await store.NamesAsync(new AssetQuery { MinSize = 0 }));
    }
}
