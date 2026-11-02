using JGlancer.Core.Model;

namespace JGlancer.Core.Tests.Model;

public sealed class NaturalSortKeyTests
{
    private static List<string> Sorted(params string[] names) => names.OrderBy(NaturalSortKey.Make, StringComparer.Ordinal).ToList();

    [Fact]
    public void NumbersSortByValue()
    {
        Assert.Equal(["IMG_1.jpg", "IMG_2.jpg", "IMG_10.jpg"], Sorted("IMG_10.jpg", "IMG_2.jpg", "IMG_1.jpg"));
        Assert.Equal(["track 3", "track 20", "track 100"], Sorted("track 100", "track 20", "track 3"));
    }

    [Fact]
    public void CaseDiacriticsAndWidthAreIgnored()
    {
        Assert.Equal(NaturalSortKey.Make("cafe.jpg"), NaturalSortKey.Make("Café.JPG"));
        Assert.Equal(["A.jpg", "b.jpg", "c.jpg"], Sorted("b.jpg", "A.jpg", "c.jpg"));
        Assert.Equal(NaturalSortKey.Make("abc"), NaturalSortKey.Make("ＡＢＣ"));
        Assert.Equal(NaturalSortKey.Make("ёлка"), NaturalSortKey.Make("ЕЛКА"));
    }

    [Fact]
    public void DecomposedAndComposedNamesShareAKey() =>
        Assert.Equal(NaturalSortKey.Make("café"), NaturalSortKey.Make("café"));

    [Fact]
    public void LeadingZerosDoNotChangeOrder() =>
        Assert.Equal(["x9", "x010", "x0011"], Sorted("x010", "x9", "x0011"));
}

public sealed class FlagAndKindTests
{
    [Fact]
    public void PressingAFlagTogglesForTheWholeSelection()
    {
        Assert.Equal(AssetFlag.Red, AssetFlags.Applying(AssetFlag.Red, [AssetFlag.None, AssetFlag.Red]));
        Assert.Equal(AssetFlag.None, AssetFlags.Applying(AssetFlag.Red, [AssetFlag.Red, AssetFlag.Red]));
        Assert.Equal(AssetFlag.Accent, AssetFlags.Applying(AssetFlag.Accent, [AssetFlag.Red]));
        Assert.Equal(AssetFlag.None, AssetFlags.Applying(AssetFlag.None, [AssetFlag.Red]));
    }

    [Fact]
    public void AtLeastOneKindStaysOn()
    {
        var photo = new HashSet<MediaKind> { MediaKind.Photo };
        Assert.Equal(photo, AssetQuery.Toggling(photo, MediaKind.Photo));
        Assert.Equal(new HashSet<MediaKind> { MediaKind.Photo, MediaKind.Video }, AssetQuery.Toggling(photo, MediaKind.Video));
        Assert.Equal(new HashSet<MediaKind> { MediaKind.Video }, AssetQuery.Toggling(new HashSet<MediaKind> { MediaKind.Photo, MediaKind.Video }, MediaKind.Photo));
    }

    [Fact]
    public void PersistedValuesNeverChange()
    {
        Assert.Equal([0, 1, 2], Enum.GetValues<MediaKind>().Select(k => (int)k));
        Assert.Equal(7, (int)AssetFlag.Purple);
    }

    [Fact]
    public void FolderTreeIsNaturallySortedAndFindsPaths()
    {
        Folder F(long id, long? parent, string path) => new(id, 1, parent, path, path.Split('/')[^1], AssetState.Present);
        var tree = FolderNode.Tree([F(1, null, ""), F(2, 1, "IMG 10"), F(3, 1, "IMG 2"), F(4, 3, "IMG 2/deep")])!;
        Assert.Equal(["IMG 2", "IMG 10"], tree.Children!.Select(c => c.Folder.Name));
        Assert.Null(tree.Find(2)!.Children);
        Assert.Equal([1L, 3, 4], tree.PathTo(4)!.Select(n => n.Id));
        Assert.Null(tree.PathTo(99));
    }

    [Fact]
    public void SidebarKeepsOnlyFoldersWithMedia()
    {
        Folder F(long id, long? parent, string path) => new(id, 1, parent, path, path.Split('/')[^1], AssetState.Present);
        // 2 is empty; 3 is empty but 4 below it has media; 5 is empty below the empty 3.
        var folders = new[] { F(1, null, ""), F(2, 1, "a"), F(3, 1, "b"), F(4, 3, "b/c"), F(5, 3, "b/d") };
        Assert.Equal([1L, 3, 4], FolderNode.WithMedia(folders, new HashSet<long> { 4 }).Select(f => f.Id));
        Assert.Equal([1L], FolderNode.WithMedia(folders, new HashSet<long>()).Select(f => f.Id));
    }
}
