using JGlancer.Core.Model;
using JGlancer.Core.Scanning;
using JGlancer.Core.Store;
using JGlancer.Core.Tests.Support;

namespace JGlancer.Platform.Tests;

/// <summary>End-to-end scans of real temporary folders (macOS ScannerTests, with NTFS file ids).</summary>
public sealed class ScannerTests
{
    /// <summary>a.jpg, b.mov, 2024/c.jpg, 2024/May/d.m4a and a non-media file, scanned.</summary>
    private static async Task<(TemporaryDirectory Folder, LibraryScanner Scanner, RootFolder Root)> SetUpAsync()
    {
        var folder = new TemporaryDirectory();
        folder.Write("a.jpg");
        folder.Write("b.mov");
        folder.Write("2024/c.jpg");
        folder.Write("2024/May/d.m4a");
        folder.Write("notes.txt");
        var scanner = Fixtures.Scanner(LibraryStore.InMemory());
        var (root, report) = await scanner.AddRootAsync(folder.Path);
        Assert.Equal(4, report.Added);
        Assert.Equal(3, report.Directories);
        return (folder, scanner, root);
    }

    private static async Task<Dictionary<string, long>> IdsAsync(LibraryScanner scanner)
    {
        var refs = await scanner.Store.AssetRefsAsync(new AssetQuery());
        return (await scanner.Store.AssetsAsync(refs.Select(r => r.Id).ToList())).ToDictionary(a => a.Name, a => a.Id);
    }

    [Fact]
    public async Task AddingARootIndexesItsTree()
    {
        var (folder, scanner, root) = await SetUpAsync();
        using var _ = folder;
        using var __ = scanner.Store;
        Assert.Equal(["", "2024", "2024/May"], (await scanner.Store.FoldersAsync(root.Id)).Select(f => f.RelativePath));
        Assert.Equal(["a.jpg", "b.mov", "c.jpg", "d.m4a"], (await IdsAsync(scanner)).Keys.Order(StringComparer.Ordinal));
        Assert.False((await scanner.ScanAsync(root.Id)).HasChanges);
        Assert.Matches("^[0-9A-F]{8}$", root.VolumeId);
    }

    [Fact]
    public async Task RenamesAndMovesOnDiskKeepAssetIds()
    {
        var (folder, scanner, root) = await SetUpAsync();
        using var _ = folder;
        using var __ = scanner.Store;
        var before = await IdsAsync(scanner);
        folder.Move("a.jpg", "renamed.jpg");
        folder.Move("2024/c.jpg", "2024/May/c.jpg");
        folder.Move("b.mov", "Elsewhere/b.mov");
        var report = await scanner.ScanAsync(root.Id);
        Assert.Equal(3, report.Moved);
        Assert.Equal(0, report.Added);
        Assert.Equal(0, (await scanner.Store.StatisticsAsync()).MissingAssets);
        var after = await IdsAsync(scanner);
        Assert.Equal(before["a.jpg"], after["renamed.jpg"]);
        Assert.Equal(before["c.jpg"], after["c.jpg"]);
        Assert.Equal(before["b.mov"], after["b.mov"]);
        Assert.Equal(folder.Full("2024/May/c.jpg"), await scanner.Store.FilePathAsync(after["c.jpg"]), ignoreCase: true);
    }

    [Fact]
    public async Task ModificationsDeletionsAndRemovedFolders()
    {
        var (folder, scanner, root) = await SetUpAsync();
        using var _ = folder;
        using var __ = scanner.Store;
        folder.Write("a.jpg", "longer content");
        folder.Remove("b.mov");
        folder.Remove("2024");
        var report = await scanner.ScanAsync(root.Id);
        Assert.Equal(1, report.Updated);
        Assert.Equal(3, report.Missing);
        Assert.Equal(["a.jpg"], (await IdsAsync(scanner)).Keys);
        Assert.Equal([""], (await scanner.Store.FoldersAsync(root.Id)).Select(f => f.RelativePath));
    }

    [Fact]
    public async Task MissingRootIsReportedWithoutTouchingRows()
    {
        var (folder, scanner, root) = await SetUpAsync();
        using var __ = scanner.Store;
        folder.Dispose();
        await Assert.ThrowsAsync<RootUnavailableException>(() => scanner.ScanAsync(root.Id));
        Assert.Equal(4, (await scanner.Store.StatisticsAsync()).PresentAssets);
    }

    [Fact]
    public async Task RootIsStoredInItsCanonicalForm()
    {
        using var folder = new TemporaryDirectory();
        folder.Write("Photos/a.jpg");
        using var store = LibraryStore.InMemory();
        var scanner = Fixtures.Scanner(store);
        var (root, _) = await scanner.AddRootAsync(folder.Full("photos") + Path.DirectorySeparatorChar);
        Assert.EndsWith(@"\Photos", root.Path, StringComparison.Ordinal);
        await Assert.ThrowsAsync<RootAlreadyExistsException>(() => scanner.AddRootAsync(folder.Full("PHOTOS")));
    }

    [Fact]
    public async Task MetadataIsExtractedFromRealMedia()
    {
        using var folder = new TemporaryDirectory();
        Fixtures.CopyTo(folder, "photo_orientation6.jpg", "tone_440_mono.wav", "clip.mov", "corrupt_garbage.jpg");
        using var store = LibraryStore.InMemory();
        var scanner = Fixtures.Scanner(store);
        await scanner.AddRootAsync(folder.Path);
        Assert.Equal(4, await scanner.ExtractMetadataAsync());
        var assets = (await store.AssetsAsync((await store.AssetRefsAsync(new AssetQuery())).Select(r => r.Id).ToList())).ToDictionary(a => a.Name);
        Assert.Equal((480, 640, 6), (assets["photo_orientation6.jpg"].Width, assets["photo_orientation6.jpg"].Height, assets["photo_orientation6.jpg"].Orientation));
        Assert.Equal(2, assets["tone_440_mono.wav"].Duration!.Value, precision: 2);
        Assert.Equal("avc1", assets["clip.mov"].Codec);
        Assert.Equal(MetadataState.Failed, assets["corrupt_garbage.jpg"].MetadataState);
        Assert.Equal(0, (await store.StatisticsAsync()).PendingMetadata);
        // The quick hash is stored with stage 2, so later moves across volumes can be recognized.
        var pendingHash = store.Read(c => Sql.Long(c, "SELECT COUNT(*) FROM asset WHERE quick_hash IS NULL"));
        Assert.Equal(0, pendingHash);
    }

    [Fact]
    public async Task DuplicatesAreSameSizeAndHashAndProvenByContent()
    {
        using var folder = new TemporaryDirectory();
        var original = folder.Write("a.jpg", "the same bytes");
        var copy = folder.Write("2024/copy of a.jpg", "the same bytes");
        folder.Write("b.jpg", "other bytes!!!");
        folder.Write("c.jpg", "the same bytez");
        using var store = LibraryStore.InMemory();
        var scanner = Fixtures.Scanner(store);
        await scanner.AddRootAsync(folder.Path);
        Assert.Empty(await store.DuplicateGroupsAsync());
        await scanner.ExtractMetadataAsync();

        var groups = await store.DuplicateGroupsAsync();
        var group = Assert.Single(groups);
        var names = (await store.AssetsAsync(group)).Select(a => a.Name).Order(StringComparer.Ordinal);
        Assert.Equal(["a.jpg", "copy of a.jpg"], names);

        Assert.True(await FileContent.SameAsync(original, copy));
        Assert.False(await FileContent.SameAsync(original, folder.Full("c.jpg")));
        Assert.False(await FileContent.SameAsync(original, original));
        Assert.False(await FileContent.SameAsync(original, folder.Full("missing.jpg")));
    }

    [Fact]
    public async Task MetadataWaitsWhilePaused()
    {
        using var folder = new TemporaryDirectory();
        Fixtures.CopyTo(folder, "photo_orientation6.jpg", "tone_440_mono.wav");
        using var store = LibraryStore.InMemory();
        var scanner = Fixtures.Scanner(store);
        await scanner.AddRootAsync(folder.Path);
        var pause = scanner.PauseMetadata();
        var nested = scanner.PauseMetadata();
        var extracting = scanner.ExtractMetadataAsync();
        await Task.Delay(300);
        Assert.False(extracting.IsCompleted);
        nested.Dispose();
        await Task.Delay(200);
        Assert.False(extracting.IsCompleted);
        pause.Dispose();
        pause.Dispose();
        Assert.Equal(2, await extracting.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}

/// <summary>Live watching on temporary folders; generous timeouts because delivery is asynchronous.</summary>
public sealed class WatcherTests
{
    private static async Task<bool> EventuallyAsync(Func<Task<bool>> condition, double seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }
            await Task.Delay(100);
        }
        return await condition();
    }

    [Fact]
    public async Task LiveEventsAreDeliveredInBatches()
    {
        using var folder = new TemporaryDirectory();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var watcher = new FolderWatcher(TimeSpan.FromMilliseconds(100));
        var received = Task.Run(async () =>
        {
            await foreach (var batch in watcher.Events(folder.Path, stop.Token))
            {
                if (batch.Any(e => e.Path.EndsWith("new.jpg", StringComparison.Ordinal)))
                {
                    return true;
                }
            }
            return false;
        });
        // The watcher starts on the thread pool, which can take a while on a busy machine (right after a build): a file
        // written before it watches gives no event. The file is touched until one arrives.
        for (var attempt = 0; attempt < 30 && !received.IsCompleted; attempt++)
        {
            folder.Write("new.jpg", attempt.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await Task.WhenAny(received, Task.Delay(300));
        }
        Assert.True(await received);
    }

    [Fact]
    public async Task WatchingKeepsTheLibraryInSync()
    {
        using var folder = new TemporaryDirectory();
        folder.Write("a.jpg");
        using var store = LibraryStore.InMemory();
        var scanner = Fixtures.Scanner(store);
        var root = await scanner.RegisterRootAsync(folder.Path);
        using var stop = new CancellationTokenSource();
        var scanned = new TaskCompletionSource();
        var watching = Task.Run(() => scanner.WatchAsync(root.Id, update =>
        {
            if (update.Events.Count == 0)
            {
                scanned.TrySetResult();
            }
        }, stop.Token));
        await scanned.Task.WaitAsync(TimeSpan.FromSeconds(10));
        async Task<HashSet<string>> Names() =>
            (await store.AssetsAsync((await store.AssetRefsAsync(new AssetQuery())).Select(r => r.Id).ToList())).Select(a => a.Name).ToHashSet();
        Assert.Equal(["a.jpg"], await Names());
        var id = (await store.AssetRefsAsync(new AssetQuery()))[0].Id;
        folder.Write("sub/new.jpg");
        folder.Move("a.jpg", "b.jpg");
        Assert.True(await EventuallyAsync(async () => (await Names()).SetEquals(["b.jpg", "new.jpg"])));
        // The rename was recognized by file id: same asset.
        Assert.Contains(id, (await store.AssetRefsAsync(new AssetQuery { Search = "b.jpg" })).Select(r => r.Id));
        folder.Remove("sub");
        Assert.True(await EventuallyAsync(async () => (await Names()).SetEquals(["b.jpg"])));
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watching);
    }
}
