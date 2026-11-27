using JGlancer.Core.Model;
using JGlancer.Core.Store;

namespace JGlancer.Core.Tests.Store;

/// <summary>Library folders' own icons (macOS D-129, MAC-052).</summary>
public sealed class FolderIconTests
{
    [Fact]
    public async Task IconsAreStoredReplacedAndRemoved()
    {
        using var store = LibraryStore.InMemory();
        var a = await store.AddRootAsync(@"C:\a");
        var b = await store.AddRootAsync(@"C:\b");
        await store.SetRootIconAsync(a.Id, new FolderIcon.Symbol("camera"));
        await store.SetRootIconAsync(b.Id, new FolderIcon.Picture([1, 2, 3]));
        var icons = await store.RootIconsAsync();
        Assert.Equal(new FolderIcon.Symbol("camera"), icons[a.Id]);
        Assert.Equal([1, 2, 3], Assert.IsType<FolderIcon.Picture>(icons[b.Id]).Jpeg);
        await store.SetRootIconAsync(a.Id, new FolderIcon.Emoji("🎸"));
        Assert.Equal(new FolderIcon.Emoji("🎸"), (await store.RootIconsAsync())[a.Id]);
        await store.SetRootIconAsync(a.Id, null);
        Assert.False((await store.RootIconsAsync()).ContainsKey(a.Id));
    }

    [Fact]
    public async Task ALibraryFolderTakesItsIconAlongWhenRemoved()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\a");
        await store.SetRootIconAsync(root.Id, new FolderIcon.Symbol("star"));
        await store.RemoveRootAsync(root.Id);
        Assert.Empty(await store.RootIconsAsync());
    }

    [Theory]
    [InlineData("🎸", "🎸")]
    [InlineData("ab🎸cd😀", "😀")]
    [InlineData("👨‍👩‍👧 x", "👨‍👩‍👧")]
    [InlineData("1#", null)]
    [InlineData("1️⃣", "1️⃣")]
    [InlineData("☀️", "☀️")]
    [InlineData("", null)]
    public void TheLastEmojiTypedIsTheIcon(string typed, string? expected) => Assert.Equal(expected, FolderIcon.LastEmoji(typed));
}
