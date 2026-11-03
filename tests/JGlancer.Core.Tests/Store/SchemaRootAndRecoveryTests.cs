using JGlancer.Core.Store;
using JGlancer.Core.Tests.Support;
using static JGlancer.Core.Tests.Store.StoreTestSupport;

namespace JGlancer.Core.Tests.Store;

public sealed class MigrationAndRootTests
{
    private static readonly string[] AllMigrations = ["v1-library", "v2-albums", "v3-search", "v4-audio-analysis"];

    [Fact]
    public async Task MigrationsCreateTheSchemaOnceAndReopenCleanly()
    {
        using var folder = new TemporaryDirectory();
        var path = Path.Combine(folder.Path, "library.sqlite");
        using (var first = LibraryStore.Open(path))
        {
            Assert.Equal(AllMigrations, await first.AppliedMigrationsAsync());
        }
        using var second = LibraryStore.Open(path);
        Assert.Equal(AllMigrations, await second.AppliedMigrationsAsync());
        Assert.Equal("wal", second.Read(c => Sql.Scalar(c, "PRAGMA journal_mode") as string));
        var tables = second.Read(c => Sql.Rows(c, "SELECT name FROM sqlite_master WHERE type = 'table'", r => r.GetString(0)));
        Assert.Superset(new HashSet<string> { "root_folder", "folder", "asset", "album", "album_item", "asset_search" }, tables.ToHashSet());
    }

    [Fact]
    public async Task RootsRejectDuplicatesAndOverlaps()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"D:\Media\Photos\");
        Assert.Equal(@"D:\Media\Photos", root.Path);
        await Assert.ThrowsAsync<RootAlreadyExistsException>(() => store.AddRootAsync(@"D:\Media\Photos"));
        await Assert.ThrowsAsync<RootAlreadyExistsException>(() => store.AddRootAsync(@"d:\media\photos"));
        await Assert.ThrowsAsync<RootOverlapsException>(() => store.AddRootAsync(@"D:\Media\Photos\2024"));
        await Assert.ThrowsAsync<RootOverlapsException>(() => store.AddRootAsync(@"D:\Media"));
        await Assert.ThrowsAsync<RootOverlapsException>(() => store.AddRootAsync(@"D:\"));
        await store.AddRootAsync(@"D:\Media\Photos 2");
        Assert.Equal(2, (await store.RootsAsync()).Count);
    }

    [Fact]
    public async Task DriveRootKeepsItsSeparatorAndName()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"E:\");
        Assert.Equal(@"E:\", root.Path);
        await store.ScanAsync(root.Id, "", [File("a.jpg", fileId: 1)], ["sub"]);
        await store.ScanAsync(root.Id, "sub", [File("b.jpg", fileId: 2)]);
        var ids = await store.IdsByNameAsync();
        Assert.Equal(@"E:\sub\b.jpg", await store.FilePathAsync(ids["b.jpg"]));
        Assert.Equal(@"E:\a.jpg", await store.FilePathAsync(ids["a.jpg"]));
        Assert.Equal("E:", (await store.FoldersAsync(root.Id))[0].Name);
    }

    [Fact]
    public async Task RemovingARootRemovesItsRowsOnly()
    {
        using var store = LibraryStore.InMemory();
        var first = await store.AddRootAsync(@"C:\a");
        var second = await store.AddRootAsync(@"C:\b");
        await store.ScanAsync(first.Id, [File("1.jpg")]);
        await store.ScanAsync(second.Id, [File("2.jpg")]);
        await store.RemoveRootAsync(first.Id);
        Assert.Equal(["2.jpg"], (await store.IdsByNameAsync()).Keys);
        Assert.Empty(await store.FoldersAsync(first.Id));
        Assert.Empty(await store.AssetRefsAsync(new() { Search = "1.jpg" }));
    }
}

public sealed class RecoveryTests
{
    [Fact]
    public async Task AHealthyLibraryOpensAsItIsAndBacksUpOncePerDay()
    {
        using var folder = new TemporaryDirectory();
        var path = Path.Combine(folder.Path, "library.sqlite");
        var backup = Path.Combine(folder.Path, "library-backup.sqlite");
        var (store, recovery) = LibraryStore.OpenWithRecovery(path, backup);
        using (store)
        {
            Assert.Null(recovery);
            await store.AddRootAsync(@"C:\lib");
            Assert.True(await store.BackupAsync(backup, TimeSpan.FromDays(1)));
            Assert.False(await store.BackupAsync(backup, TimeSpan.FromDays(1)));
            Assert.True(await store.BackupAsync(backup, TimeSpan.FromDays(1), DateTimeOffset.UtcNow.AddHours(25)));
        }
        using var copy = LibraryStore.Open(backup);
        Assert.Single(await copy.RootsAsync());
    }

    [Fact]
    public async Task ADamagedLibraryIsSetAsideAndTheBackupTakesItsPlace()
    {
        using var folder = new TemporaryDirectory();
        var path = Path.Combine(folder.Path, "library.sqlite");
        var backup = Path.Combine(folder.Path, "library-backup.sqlite");
        using (var original = LibraryStore.Open(path))
        {
            await original.AddRootAsync(@"C:\kept");
            await original.BackupAsync(backup, TimeSpan.Zero);
        }
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            System.IO.File.Delete(path + suffix);
        }
        var garbage = Enumerable.Repeat((byte)0x42, 8192).ToArray();
        System.IO.File.WriteAllBytes(path, garbage);
        var (store, recovery) = LibraryStore.OpenWithRecovery(path, backup);
        using (store)
        {
            var restored = Assert.IsType<LibraryRecovery.RestoredFromBackup>(recovery);
            Assert.Equal([@"C:\kept"], (await store.RootsAsync()).Select(r => r.Path));
            // The damaged file is kept, byte for byte.
            Assert.Equal(garbage, System.IO.File.ReadAllBytes(restored.DamagedCopy));
        }
    }

    [Fact]
    public async Task WithoutABackupANewLibraryStartsAndTheDamagedFileStays()
    {
        using var folder = new TemporaryDirectory();
        var path = Path.Combine(folder.Path, "library.sqlite");
        System.IO.File.WriteAllBytes(path, Enumerable.Repeat((byte)0x13, 4096).ToArray());
        var (store, recovery) = LibraryStore.OpenWithRecovery(path, Path.Combine(folder.Path, "missing-backup.sqlite"));
        using (store)
        {
            var empty = Assert.IsType<LibraryRecovery.StartedEmpty>(recovery);
            Assert.Empty(await store.RootsAsync());
            Assert.True(System.IO.File.Exists(empty.DamagedCopy));
        }
    }
}
