using JGlancer.Core.Model;
using JGlancer.Core.Store;
using static JGlancer.Core.Tests.Store.StoreTestSupport;

namespace JGlancer.Core.Tests.Store;

/// <summary>Diff cases: add, unchanged, modify, remove, revive, rename, move, folder removal (macOS ReconcileTests).</summary>
public sealed class ReconcileTests
{
    [Fact]
    public async Task FirstScanAddsAndSecondScanChangesNothing()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\lib");
        ScannedFile[] files = [File("a.jpg", fileId: 1), File("b.mov", fileId: 2, kind: MediaKind.Video)];
        Assert.Equal(2, (await store.ScanAsync(root.Id, files)).Added);
        var second = await store.ScanAsync(root.Id, files);
        Assert.Equal(2, second.Unchanged);
        Assert.False(second.HasChanges);
    }

    [Fact]
    public async Task ModifiedFileIsUpdatedAndQueuedForMetadataAgain()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\lib");
        await store.ScanAsync(root.Id, [File("a.jpg", size: 100, fileId: 1)]);
        var id = (await store.IdsByNameAsync())["a.jpg"];
        await store.SaveMetadataAsync([new MetadataUpdate(id, Base, MetadataState.Extracted) { Width = 10, Height = 20 }]);
        var result = await store.ScanAsync(root.Id, [File("a.jpg", size: 200, modified: 60, fileId: 1)]);
        Assert.Equal(1, result.Updated);
        var asset = (await store.AssetsAsync([id])).Single();
        Assert.Equal(200, asset.Size);
        Assert.Null(asset.Width);
        Assert.Equal(MetadataState.Pending, asset.MetadataState);
    }

    [Fact]
    public async Task RemovedFileBecomesATombstoneAndRevivesWithTheSameId()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\lib");
        await store.ScanAsync(root.Id, [File("a.jpg", fileId: 1), File("b.jpg", fileId: 2)]);
        var id = (await store.IdsByNameAsync())["a.jpg"];
        Assert.Equal(1, (await store.ScanAsync(root.Id, [File("b.jpg", fileId: 2)])).Missing);
        Assert.False((await store.IdsByNameAsync()).ContainsKey("a.jpg"));
        Assert.Equal(1, (await store.StatisticsAsync()).MissingAssets);
        var back = await store.ScanAsync(root.Id, [File("a.jpg", fileId: 1), File("b.jpg", fileId: 2)]);
        Assert.Equal(1, back.Revived);
        Assert.Equal(id, (await store.IdsByNameAsync())["a.jpg"]);
    }

    [Fact]
    public async Task RenameInTheSameFolderKeepsTheId()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\lib");
        await store.ScanAsync(root.Id, [File("old.jpg", fileId: 7)]);
        var id = (await store.IdsByNameAsync())["old.jpg"];
        var result = await store.ScanAsync(root.Id, [File("new.jpg", fileId: 7)]);
        Assert.Equal(1, result.Moved);
        Assert.Equal(0, result.Added);
        Assert.Equal(new Dictionary<string, long> { ["new.jpg"] = id }, await store.IdsByNameAsync());
    }

    [Fact]
    public async Task CaseOnlyRenameKeepsTheId()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\lib");
        await store.ScanAsync(root.Id, [File("photo.jpg", fileId: 3)]);
        var id = (await store.IdsByNameAsync())["photo.jpg"];
        var result = await store.ScanAsync(root.Id, [File("Photo.JPG", fileId: 3)]);
        Assert.Equal(1, result.Moved);
        Assert.Equal(id, (await store.IdsByNameAsync())["Photo.JPG"]);
    }

    [Fact]
    public async Task MoveToAnotherFolderKeepsTheIdWhenTheOldFolderIsScannedFirst()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\lib");
        await store.ScanAsync(root.Id, [File("x.jpg", fileId: 9)], ["sub"]);
        var id = (await store.IdsByNameAsync())["x.jpg"];
        await store.ScanAsync(root.Id, [], ["sub"]);
        Assert.Equal(1, (await store.ScanAsync(root.Id, "sub", [File("x.jpg", fileId: 9)])).Moved);
        Assert.Equal(AssetState.Present, (await store.AssetsAsync([id])).Single().State);
        Assert.Equal(@"C:\lib\sub\x.jpg", await store.FilePathAsync(id));
    }

    [Fact]
    public async Task MoveToAnotherFolderKeepsTheIdWhenTheNewFolderIsScannedFirst()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\lib");
        await store.ScanAsync(root.Id, [File("x.jpg", fileId: 9)], ["sub"]);
        var id = (await store.IdsByNameAsync())["x.jpg"];
        // The old path no longer exists (FakeProbe knows no paths), so the file-id match is a move.
        Assert.Equal(1, (await store.ScanAsync(root.Id, "sub", [File("x.jpg", fileId: 9)])).Moved);
        Assert.Equal(0, (await store.ScanAsync(root.Id, [], ["sub"])).Missing);
        Assert.Equal(@"C:\lib\sub\x.jpg", await store.FilePathAsync(id));
    }

    [Fact]
    public async Task HardLinkWithTheSameFileIdIsNotAMove()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\lib");
        await store.ScanAsync(root.Id, [File("x.jpg", fileId: 9)], ["sub"]);
        var probe = new FakeProbe { ExistingPaths = { @"C:\lib\x.jpg" } };
        var result = await store.ScanAsync(root.Id, "sub", [File("link.jpg", fileId: 9)], probe: probe);
        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Moved);
    }

    [Fact]
    public async Task CopyAcrossVolumesIsMatchedByQuickHash()
    {
        using var store = LibraryStore.InMemory();
        var source = await store.AddRootAsync(@"D:\lib", volumeId: "A");
        var target = await store.AddRootAsync(@"E:\lib", volumeId: "B");
        await store.ScanAsync(source.Id, [File("x.jpg", size: 500, fileId: 5)]);
        var id = (await store.IdsByNameAsync())["x.jpg"];
        byte[] hash = [1, 2, 3];
        await store.SaveMetadataAsync([new MetadataUpdate(id, Base, MetadataState.Extracted) { QuickHash = hash }]);
        await store.ScanAsync(source.Id, []);
        var probe = new FakeProbe { Hashes = { [@"E:\lib\x.jpg"] = hash } };
        Assert.Equal(1, (await store.ScanAsync(target.Id, [File("x.jpg", size: 500, fileId: 5)], probe: probe)).Moved);
        Assert.Equal(@"E:\lib\x.jpg", await store.FilePathAsync(id));
    }

    [Fact]
    public async Task SameFileIdOnAnotherVolumeIsNotAMatch()
    {
        using var store = LibraryStore.InMemory();
        var source = await store.AddRootAsync(@"D:\lib", volumeId: "A");
        var target = await store.AddRootAsync(@"E:\lib", volumeId: "B");
        await store.ScanAsync(source.Id, [File("x.jpg", fileId: 5)]);
        await store.ScanAsync(source.Id, []);
        Assert.Equal(1, (await store.ScanAsync(target.Id, [File("other.jpg", fileId: 5)])).Added);
    }

    [Fact]
    public async Task AmbiguousContentMatchesCreateNewAssets()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\lib");
        await store.ScanAsync(root.Id, [File("a.jpg", size: 10, fileId: 1), File("b.jpg", size: 10, fileId: 2)]);
        byte[] hash = [9];
        var ids = await store.IdsByNameAsync();
        await store.SaveMetadataAsync(ids.Values.Select(id => new MetadataUpdate(id, Base, MetadataState.Extracted) { QuickHash = hash }).ToList());
        await store.ScanAsync(root.Id, []);
        var probe = new FakeProbe { Hashes = { [@"C:\lib\c.jpg"] = hash } };
        var result = await store.ScanAsync(root.Id, [File("c.jpg", size: 10, fileId: 3)], probe: probe);
        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Moved);
    }

    [Fact]
    public async Task RemovedSubfolderTombstonesItsSubtreeAndComesBack()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\lib");
        await store.ScanAsync(root.Id, [], ["2024"]);
        await store.ScanAsync(root.Id, "2024", [File("a.jpg", fileId: 1)], ["May"]);
        await store.ScanAsync(root.Id, "2024/May", [File("b.jpg", fileId: 2)]);
        Assert.Equal(["", "2024", "2024/May"], (await store.FoldersAsync(root.Id)).Select(f => f.RelativePath));
        var removal = await store.ScanAsync(root.Id, []);
        Assert.Equal(1, removal.RemovedSubfolders);
        Assert.Equal(2, removal.Missing);
        Assert.Equal([""], (await store.FoldersAsync(root.Id)).Select(f => f.RelativePath));
        await store.ScanAsync(root.Id, [], ["2024"]);
        Assert.Equal(1, (await store.ScanAsync(root.Id, "2024", [File("a.jpg", fileId: 1)], [])).Revived);
        Assert.Equal(["", "2024"], (await store.FoldersAsync(root.Id)).Select(f => f.RelativePath));
    }
}
