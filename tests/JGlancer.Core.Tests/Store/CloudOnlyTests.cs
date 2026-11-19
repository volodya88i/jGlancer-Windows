using JGlancer.Core.Model;
using JGlancer.Core.Store;
using static JGlancer.Core.Tests.Store.StoreTestSupport;

namespace JGlancer.Core.Tests.Store;

/// <summary>Files kept only in the cloud are listed but never read until they come down (MAC-040).</summary>
public sealed class CloudOnlyTests
{
    private sealed class CountingProbe : IFileProbe
    {
        public List<string> Hashed { get; } = [];

        public bool FileExists(string path) => false;

        public byte[]? QuickHash(string path)
        {
            Hashed.Add(Path.GetFileName(path));
            return [1, 2, 3];
        }
    }

    [Fact]
    public async Task ACloudFileIsListedButNotReadUntilItComesDown()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\cloud");
        await store.ScanAsync(root.Id, [File("a.jpg") with { IsCloudOnly = true }, File("b.jpg")]);
        var ids = await store.IdsByNameAsync();
        Assert.True((await store.AssetsAsync([ids["a.jpg"]]))[0].IsCloudOnly);
        Assert.False((await store.AssetsAsync([ids["b.jpg"]]))[0].IsCloudOnly);
        // Stage 2 skips it, and it does not count as pending.
        Assert.Equal([ids["b.jpg"]], (await store.PendingMetadataAsync()).Select(p => p.Id));

        // Downloaded: an update, and stage 2 reads it now.
        var result = await store.ScanAsync(root.Id, [File("a.jpg"), File("b.jpg")]);
        Assert.Equal(1, result.Updated);
        Assert.False((await store.AssetsAsync([ids["a.jpg"]]))[0].IsCloudOnly);
        Assert.Contains(ids["a.jpg"], (await store.PendingMetadataAsync()).Select(p => p.Id));
    }

    [Fact]
    public async Task ANewCloudFileIsNeverHashed()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\cloud");
        await store.ScanAsync(root.Id, [File("old.jpg")]);
        var probe = new CountingProbe();
        // The tombstone of old.jpg has the same size and date: a local file would be hashed to compare.
        await store.ApplyDirectoryScanAsync(new DirectoryScan(root.Id, "", [File("new.jpg") with { IsCloudOnly = true }], [], Base), probe);
        Assert.Empty(probe.Hashed);
        await store.ApplyDirectoryScanAsync(new DirectoryScan(root.Id, "", [File("new.jpg") with { IsCloudOnly = true }, File("other.jpg")], [], Base), probe);
        Assert.DoesNotContain("new.jpg", probe.Hashed);
    }
}
