using JGlancer.Core.Model;

namespace JGlancer.Core.Tests.Model;

/// <summary>The list view's rows (macOS ListOutlineTests, MAC-041).</summary>
public sealed class ListOutlineTests
{
    private static Folder Folder(long id, string name, long? parent) => new(id, 1, parent, name, name, AssetState.Present);

    /// <summary>Music ▸ Album A ▸ Disc 2, Music ▸ Album B, Music ▸ Empty (no files of the query).</summary>
    private static readonly FolderNode Tree = new(Folder(1, "Music", null),
    [
        new FolderNode(Folder(2, "Album A", 1), [new FolderNode(Folder(4, "Disc 2", 2), null)]),
        new FolderNode(Folder(3, "Album B", 1), null),
        new FolderNode(Folder(5, "Empty", 1), null),
    ]);

    private static readonly AssetRef[] Refs =
    [
        new(10, MediaKind.Audio, 1),
        new(11, MediaKind.Audio, 2),
        new(12, MediaKind.Audio, 4),
        new(13, MediaKind.Audio, 3),
        new(14, MediaKind.Audio, 2),
    ];

    private static ListRow F(long id) => new(true, id);

    private static ListRow A(long id) => new(false, id);

    [Fact]
    public void FoldersComeFirstAndHoldTheirFilesInTheQueryOrder()
    {
        var outline = new ListOutline(Refs, Tree);
        Assert.Equal([F(2), F(3), A(10)], outline.Children(null));
        Assert.Equal([F(4), A(11), A(14)], outline.Children(2));
        Assert.Equal([A(12)], outline.Children(4));
        Assert.Equal([A(13)], outline.Children(3));
        // A folder without files of the query is no row.
        Assert.False(outline.Folders.ContainsKey(5));
        Assert.Equal("Album A", outline.Folders[2].Name);
    }

    [Fact]
    public void ADescendingSortReversesTheFolders() =>
        Assert.Equal([F(3), F(2), A(10)], new ListOutline(Refs, Tree, foldersDescending: true).Children(null));

    [Fact]
    public void TheFolderPathLeadsToANestedFile()
    {
        var outline = new ListOutline(Refs, Tree);
        Assert.Equal([2, 4], outline.FolderPath(12));
        Assert.Equal([3], outline.FolderPath(13));
        Assert.Empty(outline.FolderPath(10));
    }

    [Fact]
    public void WithoutSubfoldersOrATreeTheListIsFlat()
    {
        var own = new ListOutline([Refs[0]], Tree);
        Assert.Equal([A(10)], own.Children(null));
        Assert.Empty(own.Folders);
        var flat = new ListOutline(Refs, null);
        Assert.Equal(Refs.Select(r => A(r.Id)), flat.Children(null));
    }

    [Fact]
    public void AFileInAFolderTheTreeDoesNotKnowStaysOnTop() =>
        Assert.Equal([A(20)], new ListOutline([new AssetRef(20, MediaKind.Photo, 99)], Tree).Children(null));

    [Fact]
    public void UnfoldedFoldersShowTheirRowsOneLevelDeeper()
    {
        var outline = new ListOutline(Refs, Tree);
        Assert.Equal([F(2), F(3), A(10)], outline.Visible(new HashSet<long>()).Select(r => r.Row));
        var open = outline.Visible(new HashSet<long> { 2, 4 });
        Assert.Equal([F(2), F(4), A(12), A(11), A(14), F(3), A(10)], open.Select(r => r.Row));
        Assert.Equal([0, 1, 2, 1, 1, 0, 0], open.Select(r => r.Depth));
        Assert.True(open[0].IsExpanded);
        Assert.False(open[5].IsExpanded);
    }

    [Fact]
    public void TheViewModeIsRestoredAndOldStatesDefaultToTheGrid()
    {
        var state = new LibraryViewState { ViewMode = LibraryViewMode.List, ListRowHeight = 40 };
        var decoded = LibraryViewState.FromJson(state.ToJson());
        Assert.Equal((LibraryViewMode.List, 40.0), (decoded.ViewMode, decoded.ListRowHeight));
        Assert.Equal(ListLayout.DefaultColumns, decoded.ListColumns);
        var old = LibraryViewState.FromJson("{\"tileSide\": 200}");
        Assert.Equal((LibraryViewMode.Grid, 200.0), (old.ViewMode, old.TileSide));
    }
}
