using JGlancer.Core.Model;
using JGlancer.Core.Store;
using JGlancer.Core.Tests.Store;

namespace JGlancer.Core.Tests.Model;

/// <summary>The size map's tree and layout, and the two places of the side modules (macOS D-127, MAC-048).</summary>
public sealed class SizeMapTests
{
    private static FolderNode Node(long id, string name, params FolderNode[] children) =>
        new(new Folder(id, 1, null, name, name, AssetState.Present), children.Length == 0 ? null : children);

    private static SizeEntry Entry(long id, long folder, long size, string name = "a.jpg", MediaKind kind = MediaKind.Photo) =>
        new(id, folder, size, kind, name);

    [Fact]
    public void FoldersSumTheirFilesAndChildrenComeLargestFirst()
    {
        var root = Node(1, "root", Node(2, "small"), Node(3, "big"), Node(4, "empty"));
        var tree = SizeTree.Build(root, [Entry(10, 1, 5), Entry(11, 2, 3), Entry(12, 3, 40), Entry(13, 3, 2), Entry(14, 2, 0)]);
        Assert.Equal(1, tree.Root!.Id);
        Assert.Equal(50, tree.Root.Size);
        var children = tree.Root.Children.Select(i => tree.Nodes[i]).ToList();
        // big 42, the file 5, small 3; the empty folder and the 0-byte file are left out.
        Assert.Equal([(true, 3L), (false, 10L), (true, 2L)], children.Select(n => (n.IsFolder, n.Id)));
        Assert.Equal([42L, 5, 3], children.Select(n => n.Size));
        Assert.DoesNotContain(tree.Nodes, n => n.Id == 14 && !n.IsFolder);
        Assert.DoesNotContain(tree.Nodes, n => n.Id == 4 && n.IsFolder);
    }

    [Fact]
    public void AFolderWithoutFilesHasAnEmptyTree()
    {
        Assert.True(SizeTree.Build(Node(1, "root", Node(2, "a")), [Entry(1, 9, 100)]).IsEmpty);
    }

    [Fact]
    public void TheExtensionIsLowercasedWithoutItsDot()
    {
        Assert.Equal("heic", Entry(1, 1, 1, "IMG_1.HEIC").Extension);
        Assert.Equal("", Entry(1, 1, 1, "README").Extension);
    }

    [Fact]
    public void SquarifiedAreasFollowTheValuesAndFillTheRectangle()
    {
        var rect = new MapRect(0, 0, 600, 400);
        var parts = SizeMapLayout.Squarify([6, 6, 4, 3, 2, 2, 1], rect);
        var total = 24.0;
        double[] values = [6, 6, 4, 3, 2, 2, 1];
        for (var i = 0; i < parts.Count; i++)
        {
            Assert.Equal(600 * 400 * values[i] / total, parts[i].Width * parts[i].Height, 6);
            Assert.True(parts[i].X >= -1e-9 && parts[i].Right <= 600 + 1e-9 && parts[i].Y >= -1e-9 && parts[i].Bottom <= 400 + 1e-9);
        }
        // The first, largest rectangles are close to squares (the point of squarifying).
        Assert.True(Math.Max(parts[0].Width / parts[0].Height, parts[0].Height / parts[0].Width) < 2);
    }

    [Fact]
    public void FoldersGetANameBandAndPaddingAndHitsFindTheDeepestNode()
    {
        var root = Node(1, "root", Node(2, "sub"));
        var tree = SizeTree.Build(root, [Entry(10, 1, 50), Entry(11, 2, 50)]);
        var layout = SizeMapLayout.Make(tree, new MapRect(0, 0, 400, 200), header: 16, padding: 2, minSide: 6);
        var rootBox = layout.Boxes[0];
        Assert.Equal(0, rootBox.Node);
        Assert.Equal(new MapRect(0, 0, 400, 16), rootBox.Header);
        var file = tree.FileNodes()[10];
        var rect = layout.Rects[file]!.Value;
        Assert.True(rect.Y >= 16 + 2 && rect.X >= 2);
        Assert.Equal(file, layout.Hit(rect.X + 1, rect.Y + 1, tree));
        Assert.Equal(0, layout.Hit(1, 1, tree));
        Assert.Null(layout.Hit(500, 1, tree));
    }

    [Fact]
    public void NothingIsLaidOutInsideTinyFolders()
    {
        var root = Node(1, "root", Node(2, "sub"));
        var tree = SizeTree.Build(root, [Entry(11, 2, 50)]);
        var layout = SizeMapLayout.Make(tree, new MapRect(0, 0, 8, 8), header: 16, padding: 2, minSide: 6);
        Assert.Single(layout.Boxes);
    }

    [Fact]
    public void AModuleShownWhereTheOtherIsTakesTheFreePlace()
    {
        var state = new LibraryViewState();
        Assert.Equal(ModuleSlot.Right, state.SlotOf(LibraryModule.Preview));
        Assert.Null(state.SlotOf(LibraryModule.SizeMap));
        state.SizeMapSlot = ModuleSlot.Right;
        state.SetVisible(LibraryModule.SizeMap, true);
        Assert.Equal(ModuleSlot.Bottom, state.SlotOf(LibraryModule.SizeMap));
        Assert.Equal(LibraryModule.Preview, state.ModuleIn(ModuleSlot.Right));
        Assert.Equal(LibraryModule.SizeMap, state.ModuleIn(ModuleSlot.Bottom));
    }

    [Fact]
    public void MovingAModuleToTheOtherOnesPlaceSwapsThem()
    {
        var state = new LibraryViewState();
        state.SetVisible(LibraryModule.SizeMap, true);
        state.Move(LibraryModule.SizeMap);
        Assert.Equal(ModuleSlot.Right, state.SlotOf(LibraryModule.SizeMap));
        Assert.Equal(ModuleSlot.Bottom, state.SlotOf(LibraryModule.Preview));
        state.SetVisible(LibraryModule.Preview, false);
        state.Move(LibraryModule.SizeMap);
        Assert.Equal(ModuleSlot.Bottom, state.SlotOf(LibraryModule.SizeMap));
        // Hidden, the preview kept its place; shown again where the map is, it takes the other one.
        state.SetVisible(LibraryModule.Preview, true);
        Assert.Equal(ModuleSlot.Right, state.SlotOf(LibraryModule.Preview));
    }

    [Fact]
    public void TheNewPlacesAreSavedAndOldStateKeepsItsDefaults()
    {
        var state = new LibraryViewState { SizeMapVisible = true, SizeMapSlot = ModuleSlot.Right, PreviewSlot = ModuleSlot.Bottom, BottomHeight = 333, SidebarCollapsed = true };
        Assert.Equal(state, LibraryViewState.FromJson(state.ToJson()) with { Kinds = state.Kinds, SelectedIds = state.SelectedIds, HiddenFolders = state.HiddenFolders, PinnedFolders = state.PinnedFolders, ListColumns = state.ListColumns });
        var old = LibraryViewState.FromJson("""{"previewVisible": false}""");
        Assert.Equal((ModuleSlot.Right, false, ModuleSlot.Bottom, 280.0, false), (old.PreviewSlot, old.SizeMapVisible, old.SizeMapSlot, old.BottomHeight, old.SidebarCollapsed));
    }

    [Fact]
    public async Task TheStoreGivesTheFilesOfAFolderWithItsFiltersButNoneForAnAlbum()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(@"C:\lib");
        await StoreTestSupport.ScanAsync(store, root.Id, "", [StoreTestSupport.File("a.jpg", size: 1000), StoreTestSupport.File("b.mov", size: 5000, kind: MediaKind.Video)], ["sub"]);
        await StoreTestSupport.ScanAsync(store, root.Id, "sub", [StoreTestSupport.File("c.jpg", size: 200)]);
        var top = (await store.FoldersAsync(root.Id)).First(f => f.ParentId is null).Id;
        var all = await store.SizeEntriesAsync(new AssetQuery { Scope = QueryScope.InFolder(top, true) });
        Assert.Equal(["a.jpg", "b.mov", "c.jpg"], all.Select(e => e.Name).Order(StringComparer.Ordinal));
        Assert.Equal(5000, all.First(e => e.Name == "b.mov").Size);
        var photos = await store.SizeEntriesAsync(new AssetQuery { Scope = QueryScope.InFolder(top, true), Kinds = new HashSet<MediaKind> { MediaKind.Photo }, MinSize = 500 });
        Assert.Equal(["a.jpg"], photos.Select(e => e.Name));
        Assert.Empty(await store.SizeEntriesAsync(new AssetQuery { Scope = QueryScope.Album(1) }));
    }
}
