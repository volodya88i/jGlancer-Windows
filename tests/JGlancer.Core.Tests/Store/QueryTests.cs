using JGlancer.Core.Model;
using JGlancer.Core.Store;
using static JGlancer.Core.Tests.Store.StoreTestSupport;

namespace JGlancer.Core.Tests.Store;

public sealed class QueryTests
{
    /// <summary>
    /// C:\lib: IMG_10.jpg (photo, 3 KB), IMG_2.jpg (photo, 1 KB), clip.mov (video, 2 KB), song.m4a (audio);
    /// C:\lib\sub: IMG_1.jpg; C:\lib\sub\deep: z.jpg.
    /// </summary>
    private static async Task<(LibraryStore Store, RootFolder Root)> LibraryAsync()
    {
        var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\lib");
        await store.ScanAsync(root.Id, [
            File("IMG_10.jpg", size: 3000, modified: 30, fileId: 1),
            File("IMG_2.jpg", size: 1000, modified: 10, fileId: 2),
            File("clip.mov", size: 2000, modified: 20, fileId: 3, kind: MediaKind.Video),
            File("song.m4a", size: 500, modified: 40, fileId: 4, kind: MediaKind.Audio),
        ], ["sub"]);
        await store.ScanAsync(root.Id, "sub", [File("IMG_1.jpg", fileId: 5)], ["deep"]);
        await store.ScanAsync(root.Id, "sub/deep", [File("z.jpg", fileId: 6)]);
        return (store, root);
    }

    private static async Task<long> TopFolderAsync(LibraryStore store, RootFolder root) =>
        (await store.FoldersAsync(root.Id)).Single(f => f.RelativePath.Length == 0).Id;

    [Fact]
    public async Task SortsAndDirections()
    {
        var (store, root) = await LibraryAsync();
        using var _ = store;
        var scope = QueryScope.InFolder(await TopFolderAsync(store, root), includeSubfolders: false);
        Assert.Equal(["clip.mov", "IMG_2.jpg", "IMG_10.jpg", "song.m4a"], await store.NamesAsync(new AssetQuery { Scope = scope }));
        Assert.Equal(["IMG_10.jpg", "clip.mov", "IMG_2.jpg", "song.m4a"], await store.NamesAsync(new AssetQuery { Scope = scope, Sort = AssetSort.Size, Ascending = false }));
        Assert.Equal(["IMG_2.jpg", "clip.mov", "IMG_10.jpg", "song.m4a"], await store.NamesAsync(new AssetQuery { Scope = scope, Sort = AssetSort.DateModified }));
        Assert.Equal(["IMG_2.jpg", "IMG_10.jpg", "clip.mov", "song.m4a"], await store.NamesAsync(new AssetQuery { Scope = scope, Sort = AssetSort.Kind }));
    }

    [Fact]
    public async Task CapturedDateFallsBackToModificationDate()
    {
        var (store, root) = await LibraryAsync();
        using var _ = store;
        var song = (await store.IdsByNameAsync())["song.m4a"];
        // The song was "captured" before everything else.
        await store.SaveMetadataAsync([new MetadataUpdate(song, Base.AddSeconds(40), MetadataState.Extracted) { CapturedAt = Base }]);
        var scope = QueryScope.InFolder(await TopFolderAsync(store, root), includeSubfolders: false);
        Assert.Equal(["song.m4a", "IMG_2.jpg", "clip.mov", "IMG_10.jpg"], await store.NamesAsync(new AssetQuery { Scope = scope, Sort = AssetSort.DateCaptured }));
    }

    [Fact]
    public async Task KindFilterAndScopes()
    {
        var (store, root) = await LibraryAsync();
        using var _ = store;
        var sub = (await store.FoldersAsync(root.Id)).Single(f => f.RelativePath == "sub");
        Assert.Equal(6, (await store.AssetRefsAsync(new AssetQuery())).Count);
        Assert.Equal(4, (await store.AssetRefsAsync(new AssetQuery { Scope = QueryScope.Root(root.Id), Kinds = new HashSet<MediaKind> { MediaKind.Photo } })).Count);
        var avKinds = (await store.AssetRefsAsync(new AssetQuery { Kinds = new HashSet<MediaKind> { MediaKind.Video, MediaKind.Audio } })).Select(r => r.Kind).Order();
        Assert.Equal([MediaKind.Video, MediaKind.Audio], avKinds);
        Assert.Empty(await store.AssetRefsAsync(new AssetQuery { Kinds = new HashSet<MediaKind>() }));
        Assert.Equal(["IMG_1.jpg"], await store.NamesAsync(new AssetQuery { Scope = QueryScope.InFolder(sub.Id, false) }));
        Assert.Equal(["IMG_1.jpg", "z.jpg"], await store.NamesAsync(new AssetQuery { Scope = QueryScope.InFolder(sub.Id, true) }));
    }

    [Fact]
    public async Task SubfolderRangeDoesNotLeakIntoSiblingsWithTheSamePrefix()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\lib");
        await store.ScanAsync(root.Id, [], ["a", "a b", "ab"]);
        await store.ScanAsync(root.Id, "a", [File("1.jpg", fileId: 1)]);
        await store.ScanAsync(root.Id, "a b", [File("2.jpg", fileId: 2)]);
        await store.ScanAsync(root.Id, "ab", [File("3.jpg", fileId: 3)]);
        var folderA = (await store.FoldersAsync(root.Id)).Single(f => f.RelativePath == "a");
        Assert.Equal(["1.jpg"], await store.NamesAsync(new AssetQuery { Scope = QueryScope.InFolder(folderA.Id, true) }));
    }

    [Fact]
    public async Task TombstonesAreHiddenFromQueries()
    {
        var (store, root) = await LibraryAsync();
        using var _ = store;
        await store.ScanAsync(root.Id, [File("IMG_2.jpg", size: 1000, modified: 10, fileId: 2)], ["sub"]);
        Assert.Equal(3, (await store.AssetRefsAsync(new AssetQuery { Scope = QueryScope.Root(root.Id) })).Count);
    }

    [Fact]
    public async Task FullRowsComeBackInRequestedOrderWithPaths()
    {
        var (store, _) = await LibraryAsync();
        using var __ = store;
        var ids = await store.IdsByNameAsync();
        List<long> order = [ids["z.jpg"], ids["clip.mov"]];
        Assert.Equal(["z.jpg", "clip.mov"], (await store.AssetsAsync([.. order, 999])).Select(a => a.Name));
        Assert.Equal(@"C:\lib\sub\deep\z.jpg", await store.FilePathAsync(order[0]));
        Assert.Equal(@"C:\lib\sub\deep\z.jpg", (await store.AssetsAsync([order[0]])).Single().Path);
    }

    [Fact]
    public async Task TotalsAddUp()
    {
        var (store, root) = await LibraryAsync();
        using var _ = store;
        var ids = await store.IdsByNameAsync();
        var photos = await store.TotalsAsync(new AssetQuery { Scope = QueryScope.Root(root.Id), Kinds = new HashSet<MediaKind> { MediaKind.Photo } });
        Assert.Equal((4, 3000L + 1000 + 100 + 100), photos);
        Assert.Equal(0, (await store.TotalsAsync(new AssetQuery { Kinds = new HashSet<MediaKind>() })).Assets);
        Assert.Equal(2500, await store.TotalSizeAsync([ids["clip.mov"], ids["song.m4a"]]));
    }

    [Fact]
    public async Task PendingMetadataIsSavedOnlyWhileTheFileIsUnchanged()
    {
        var (store, _) = await LibraryAsync();
        using var __ = store;
        var pending = await store.PendingMetadataAsync(limit: 100);
        Assert.Equal(6, pending.Count);
        var first = pending[0];
        await store.SaveMetadataAsync([new MetadataUpdate(first.Id, Base.AddSeconds(9999), MetadataState.Extracted) { Width = 1 }]);
        Assert.Equal(6, (await store.StatisticsAsync()).PendingMetadata);
        await store.SaveMetadataAsync([new MetadataUpdate(first.Id, first.ModifiedAt, MetadataState.Extracted) { Width = 1 }]);
        Assert.Equal(5, (await store.StatisticsAsync()).PendingMetadata);
        Assert.Equal(5, (await store.PendingMetadataAsync(afterId: first.Id, limit: 100)).Count);
    }

    [Fact]
    public async Task ObservationEmitsInitialValueAndChanges()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\lib");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var values = store.ObserveAssetRefs(new AssetQuery(), timeout.Token).GetAsyncEnumerator(timeout.Token);
        Assert.True(await values.MoveNextAsync());
        Assert.Empty(values.Current);
        await store.ScanAsync(root.Id, [File("a.jpg", fileId: 1)]);
        Assert.True(await values.MoveNextAsync());
        Assert.Single(values.Current);
    }

    [Fact]
    public async Task SearchFindsSubstringsInAnyCaseIncludingCyrillic()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\lib");
        await store.ScanAsync(root.Id, [
            File("IMG_1234 Отпуск.jpg", fileId: 1), File("IMG_99.jpg", fileId: 2), File("50%_off.png", fileId: 3),
            File("Café.mov", fileId: 4, kind: MediaKind.Video),
        ]);
        async Task<List<string>> Find(string text) => (await store.NamesAsync(new AssetQuery { Search = text })).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["IMG_1234 Отпуск.jpg"], await Find("отпуск"));
        Assert.Equal(["IMG_1234 Отпуск.jpg"], await Find("G_12"));
        Assert.Equal(["IMG_1234 Отпуск.jpg", "IMG_99.jpg"], await Find("img"));
        Assert.Equal(["Café.mov"], await Find("cafe"));
        // Short text uses LIKE, with % and _ taken literally.
        Assert.Equal(["50%_off.png"], await Find("%_"));
        Assert.Equal(["IMG_99.jpg"], await Find("99"));
        Assert.Equal(4, (await Find("  ")).Count);
        // Quotes in the text do not break the query.
        Assert.Empty(await Find("a\"b"));
    }
}

public sealed class LocateTests
{
    [Fact]
    public async Task PathsResolveToRootFolderAndAsset()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\lib");
        await store.ScanAsync(root.Id, [File("a.jpg", fileId: 1)], ["sub"]);
        await store.ScanAsync(root.Id, "sub", [File("b.jpg", fileId: 2)]);
        var ids = await store.IdsByNameAsync();
        var file = await store.LocationAsync(@"C:\lib\sub\b.jpg", isDirectory: false);
        Assert.Equal(root.Id, file?.RootId);
        Assert.Equal(ids["b.jpg"], file?.AssetId);
        // Windows paths compare without case.
        Assert.Equal(ids["b.jpg"], (await store.LocationAsync(@"c:\LIB\sub\b.jpg", isDirectory: false))?.AssetId);
        var folder = await store.LocationAsync(@"C:\lib\sub", isDirectory: true);
        Assert.NotNull(folder);
        Assert.True(folder.FolderId is not null && folder.AssetId is null);
        Assert.NotNull((await store.LocationAsync(@"C:\lib", isDirectory: true))?.FolderId);
        var unknown = await store.LocationAsync(@"C:\lib\sub\new.jpg", isDirectory: false);
        Assert.NotNull(unknown);
        Assert.True(unknown.AssetId is null && unknown.FolderId == folder.FolderId);
        Assert.Null(await store.LocationAsync(@"C:\library\a.jpg", isDirectory: false));
        Assert.Null(await store.LocationAsync(@"D:\elsewhere\a.jpg", isDirectory: false));
    }
}
