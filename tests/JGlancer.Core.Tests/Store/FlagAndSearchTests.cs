using JGlancer.Core.Model;
using JGlancer.Core.Store;

namespace JGlancer.Core.Tests.Store;

/// <summary>Port of the macOS FlagAndSearchTests.</summary>
public sealed class FlagAndSearchTests
{
    /// <summary>C:\lib: IMG_1234 Отпуск.jpg, IMG_99.jpg, 50%_off.png, clip.mov.</summary>
    private static async Task<(LibraryStore Store, Dictionary<string, long> Ids)> LibraryAsync()
    {
        var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\lib");
        await store.ScanAsync(root.Id, [
            StoreTestSupport.File("IMG_1234 Отпуск.jpg"), StoreTestSupport.File("IMG_99.jpg"), StoreTestSupport.File("50%_off.png"),
            StoreTestSupport.File("clip.mov", kind: MediaKind.Video),
        ], []);
        return (store, await store.IdsByNameAsync());
    }

    private static async Task<List<string>> NamesAsync(LibraryStore store, AssetQuery query)
    {
        var refs = await store.AssetRefsAsync(query);
        return [.. (await store.AssetsAsync([.. refs.Select(r => r.Id)])).Select(a => a.Name).Order(StringComparer.Ordinal)];
    }

    [Fact]
    public async Task FlagsAreSetFilteredAndRestored()
    {
        var (store, ids) = await LibraryAsync();
        using var _ = store;
        long[] files = [ids["IMG_99.jpg"], ids["clip.mov"]];
        var previous = await store.SetFlagAsync(AssetFlag.Red, files);
        Assert.All(previous.Values, flag => Assert.Equal(AssetFlag.None, flag));
        await store.SetFlagAsync(AssetFlag.Accent, [ids["50%_off.png"]]);
        Assert.Equal(["50%_off.png", "IMG_99.jpg", "clip.mov"], await NamesAsync(store, new AssetQuery { Flags = FlagFilter.Flagged }));
        Assert.Equal(["IMG_99.jpg", "clip.mov"], await NamesAsync(store, new AssetQuery { Flags = new FlagFilter(AssetFlag.Red) }));
        Assert.Equal(AssetFlag.Red, (await store.AssetsAsync([files[0]]))[0].Flag);
        Assert.Equal(AssetFlag.Red, (await store.FlagsAsync(files))[files[1]]);
        await store.RestoreFlagsAsync(previous);
        Assert.Empty(await NamesAsync(store, new AssetQuery { Flags = new FlagFilter(AssetFlag.Red) }));
    }

    [Fact]
    public void PressingAFlagTogglesForTheWholeSelection()
    {
        Assert.Equal(AssetFlag.Red, AssetFlags.Applying(AssetFlag.Red, [AssetFlag.None, AssetFlag.Red]));
        Assert.Equal(AssetFlag.None, AssetFlags.Applying(AssetFlag.Red, [AssetFlag.Red, AssetFlag.Red]));
        Assert.Equal(AssetFlag.Accent, AssetFlags.Applying(AssetFlag.Accent, [AssetFlag.Red]));
        Assert.Equal(AssetFlag.None, AssetFlags.Applying(AssetFlag.None, [AssetFlag.Red]));
    }

    [Fact]
    public async Task SearchFindsSubstringsInAnyCaseAndFollowsRenames()
    {
        var (store, _) = await LibraryAsync();
        using var __ = store;
        Assert.Equal(["IMG_1234 Отпуск.jpg"], await NamesAsync(store, new AssetQuery { Search = "отпуск" }));
        Assert.Equal(["IMG_1234 Отпуск.jpg"], await NamesAsync(store, new AssetQuery { Search = "G_12" }));
        Assert.Equal(["IMG_1234 Отпуск.jpg", "IMG_99.jpg"], await NamesAsync(store, new AssetQuery { Search = "img" }));
        // Short text uses LIKE, with % and _ taken literally.
        Assert.Equal(["50%_off.png"], await NamesAsync(store, new AssetQuery { Search = "%_" }));
        Assert.Equal(["IMG_99.jpg"], await NamesAsync(store, new AssetQuery { Search = "99" }));
        Assert.Equal(4, (await NamesAsync(store, new AssetQuery { Search = "  " })).Count);
        // Quotes in the text do not break the query.
        Assert.Empty(await NamesAsync(store, new AssetQuery { Search = "a\"b" }));
        await store.MoveAssetAsync(@"C:\lib\clip.mov", @"C:\lib\Holiday clip.mov");
        Assert.Equal(["Holiday clip.mov"], await NamesAsync(store, new AssetQuery { Search = "holiday" }));
        Assert.Empty(await NamesAsync(store, new AssetQuery { Flags = FlagFilter.Flagged, Search = "clip" }));
    }

    [Fact]
    public async Task AlbumsApplyFlagsAndSearch()
    {
        var (store, ids) = await LibraryAsync();
        using var _ = store;
        var album = await store.CreateAlbumAsync("A");
        await store.AddToAlbumAsync(album.Id, [.. ids.Values]);
        await store.SetFlagAsync(AssetFlag.Blue, [ids["IMG_99.jpg"]]);
        Assert.Equal(["IMG_99.jpg"], await NamesAsync(store, new AssetQuery { Scope = QueryScope.Album(album.Id), Flags = FlagFilter.Flagged }));
        Assert.Equal(["IMG_1234 Отпуск.jpg"], await NamesAsync(store, new AssetQuery { Scope = QueryScope.Album(album.Id), Search = "отп" }));
    }
}
