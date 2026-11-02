using JGlancer.Core.Model;
using JGlancer.Core.Store;

namespace JGlancer.Core.Tests.Store;

/// <summary>Port of the macOS AlbumTests.</summary>
public sealed class AlbumTests
{
    private static async Task<(LibraryStore Store, Dictionary<string, long> Ids)> LibraryAsync()
    {
        var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\lib");
        await store.ScanAsync(root.Id, [
            StoreTestSupport.File("a.jpg"), StoreTestSupport.File("b.jpg", size: 200),
            StoreTestSupport.File("c.mov", size: 300, kind: MediaKind.Video), StoreTestSupport.File("d.mp3", size: 400, kind: MediaKind.Audio),
        ], ["sub"]);
        return (store, await store.IdsByNameAsync());
    }

    private static async Task<List<long>> RefsAsync(LibraryStore store, long album, params MediaKind[] kinds) =>
        [.. (await store.AssetRefsAsync(new AssetQuery
        {
            Scope = QueryScope.Album(album),
            Kinds = kinds.Length > 0 ? kinds.ToHashSet() : AssetQuery.AllKinds,
        })).Select(r => r.Id)];

    [Fact]
    public async Task ItemsKeepTheirOrderSkipDuplicatesAndFollowTheKindFilter()
    {
        var (store, ids) = await LibraryAsync();
        using var _ = store;
        var album = await store.CreateAlbumAsync("Trip");
        Assert.Equal([ids["c.mov"], ids["a.jpg"]], await store.AddToAlbumAsync(album.Id, [ids["c.mov"], ids["a.jpg"], ids["c.mov"]]));
        Assert.Equal([ids["b.jpg"]], await store.AddToAlbumAsync(album.Id, [ids["a.jpg"], ids["b.jpg"]]));
        Assert.Equal([ids["c.mov"], ids["a.jpg"], ids["b.jpg"]], await RefsAsync(store, album.Id));
        Assert.Equal([ids["a.jpg"], ids["b.jpg"]], await RefsAsync(store, album.Id, MediaKind.Photo));
        var totals = await store.TotalsAsync(new AssetQuery { Scope = QueryScope.Album(album.Id) });
        Assert.Equal(3, totals.Assets);
        Assert.Equal(600, totals.Bytes);
        var listed = await store.AlbumsAsync();
        Assert.Equal(3, listed[0].ItemCount);
        Assert.Equal(ids["c.mov"], listed[0].CoverAssetId);
    }

    [Fact]
    public async Task MembershipSurvivesMovesRenamesAndARecycleBinRoundTrip()
    {
        var (store, ids) = await LibraryAsync();
        using var _ = store;
        var album = await store.CreateAlbumAsync("Keep");
        await store.AddToAlbumAsync(album.Id, [ids["a.jpg"], ids["b.jpg"]]);
        await store.MoveAssetAsync(@"C:\lib\a.jpg", @"C:\lib\sub\renamed.jpg");
        Assert.Equal([ids["a.jpg"], ids["b.jpg"]], await RefsAsync(store, album.Id));
        await store.MarkMissingAsync(@"C:\lib\b.jpg");
        // A vanished file leaves the album's view and count, but keeps its place for a return.
        Assert.Equal([ids["a.jpg"]], await RefsAsync(store, album.Id));
        Assert.Equal(1, (await store.AlbumsAsync())[0].ItemCount);
        await store.ReviveAsync(@"C:\lib\b.jpg");
        Assert.Equal([ids["a.jpg"], ids["b.jpg"]], await RefsAsync(store, album.Id));
        Assert.Equal(2, (await store.AlbumsAsync())[0].ItemCount);
    }

    [Fact]
    public async Task OldPlaceholdersArePurgedAfterThirtyDays()
    {
        var (store, ids) = await LibraryAsync();
        using var _ = store;
        var album = await store.CreateAlbumAsync("Old");
        await store.AddToAlbumAsync(album.Id, [ids["a.jpg"], ids["b.jpg"]]);
        var then = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        await store.MarkMissingAsync(@"C:\lib\a.jpg", then);
        Assert.Equal(0, await store.PurgeAlbumPlaceholdersAsync(then.AddDays(29)));
        Assert.Equal(1, await store.PurgeAlbumPlaceholdersAsync(then.AddDays(31)));
        Assert.Equal([ids["b.jpg"]], await store.AlbumOrderAsync(album.Id));
    }

    [Fact]
    public async Task RemoveAndRestoreReorderCoverAndDelete()
    {
        var (store, ids) = await LibraryAsync();
        using var _ = store;
        var album = await store.CreateAlbumAsync("Order");
        List<long> all = [ids["a.jpg"], ids["b.jpg"], ids["c.mov"], ids["d.mp3"]];
        await store.AddToAlbumAsync(album.Id, all);
        var removed = await store.RemoveFromAlbumAsync(album.Id, [ids["b.jpg"], ids["d.mp3"]]);
        Assert.Equal([ids["a.jpg"], ids["c.mov"]], await RefsAsync(store, album.Id));
        await store.RestoreToAlbumAsync(album.Id, removed);
        Assert.Equal(all, await RefsAsync(store, album.Id));
        await store.MoveInAlbumAsync(album.Id, [ids["d.mp3"], ids["c.mov"]], ids["a.jpg"]);
        Assert.Equal([ids["c.mov"], ids["d.mp3"], ids["a.jpg"], ids["b.jpg"]], await RefsAsync(store, album.Id));
        await store.MoveInAlbumAsync(album.Id, [ids["c.mov"]], null);
        Assert.Equal(ids["c.mov"], (await RefsAsync(store, album.Id))[^1]);
        await store.SetAlbumCoverAsync(album.Id, ids["b.jpg"]);
        Assert.Equal(ids["b.jpg"], (await store.AlbumsAsync())[0].CoverAssetId);
        await store.RenameAlbumAsync(album.Id, "Renamed");
        Assert.Equal(["Renamed"], (await store.AlbumsAsync()).Select(a => a.Name));
        await store.DeleteAlbumAsync(album.Id);
        Assert.Empty(await store.AlbumsAsync());
        Assert.Equal(4, (await store.IdsByNameAsync()).Count);
    }

    [Fact]
    public async Task AReplacedFilesRowLeavesItsAlbums()
    {
        var (store, ids) = await LibraryAsync();
        using var _ = store;
        var album = await store.CreateAlbumAsync("Replace");
        await store.AddToAlbumAsync(album.Id, [ids["a.jpg"], ids["b.jpg"]]);
        // b moves over a after a went to the Recycle Bin (Replace): a's tombstone row goes with its album items.
        await store.MarkMissingAsync(@"C:\lib\a.jpg");
        await store.MoveAssetAsync(@"C:\lib\b.jpg", @"C:\lib\a.jpg");
        Assert.Equal([ids["b.jpg"]], await store.AlbumOrderAsync(album.Id));
    }

    [Fact]
    public async Task MembershipsTellWhichAlbumsHoldAFile()
    {
        var (store, ids) = await LibraryAsync();
        using var _ = store;
        var one = await store.CreateAlbumAsync("One");
        var two = await store.CreateAlbumAsync("Two");
        await store.AddToAlbumAsync(one.Id, [ids["a.jpg"]]);
        await store.AddToAlbumAsync(two.Id, [ids["a.jpg"], ids["b.jpg"]]);
        var memberships = await store.AlbumMembershipsAsync([ids["a.jpg"], ids["b.jpg"], ids["c.mov"]]);
        Assert.Equal(new HashSet<long> { one.Id, two.Id }, memberships[ids["a.jpg"]]);
        Assert.Equal(new HashSet<long> { two.Id }, memberships[ids["b.jpg"]]);
        Assert.False(memberships.ContainsKey(ids["c.mov"]));
    }
}
