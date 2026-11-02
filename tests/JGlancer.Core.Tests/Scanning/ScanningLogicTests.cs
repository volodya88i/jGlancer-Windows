using JGlancer.Core.Model;
using JGlancer.Core.Scanning;
using JGlancer.Core.Tests.Support;

namespace JGlancer.Core.Tests.Scanning;

public sealed class ClassifierTests
{
    [Theory]
    [InlineData("a.jpg", MediaKind.Photo)]
    [InlineData("B.JPG", MediaKind.Photo)]
    [InlineData("c.heic", MediaKind.Photo)]
    [InlineData("d.png", MediaKind.Photo)]
    [InlineData("e.dng", MediaKind.Photo)]
    [InlineData("e.CR3", MediaKind.Photo)]
    [InlineData("f.mov", MediaKind.Video)]
    [InlineData("g.mp4", MediaKind.Video)]
    [InlineData("h.m4v", MediaKind.Video)]
    [InlineData("i.mkv", MediaKind.Video)]
    [InlineData("j.webm", MediaKind.Video)]
    [InlineData("k.m4a", MediaKind.Audio)]
    [InlineData("l.mp3", MediaKind.Audio)]
    [InlineData("m.wav", MediaKind.Audio)]
    [InlineData("n.flac", MediaKind.Audio)]
    [InlineData("o.ogg", MediaKind.Audio)]
    [InlineData("p.opus", MediaKind.Audio)]
    public void MediaFilesAreClassified(string name, MediaKind kind) => Assert.Equal(kind, MediaClassifier.Classify(name)?.Kind);

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("doc.pdf")]
    [InlineData("no_extension")]
    [InlineData(".jpg")]
    [InlineData("archive.zip")]
    [InlineData("page.html")]
    [InlineData("desktop.ini")]
    public void OtherFilesAreIgnored(string name) => Assert.Null(MediaClassifier.Classify(name));

    [Fact]
    public void TypeIdentifiersFollowMacOS() => Assert.Equal("public.jpeg", MediaClassifier.Classify("x.JPEG")?.TypeIdentifier);
}

public sealed class ChangePlannerTests
{
    private const string Root = @"C:\lib";

    private static FileSystemEvent E(FileSystemChange change, string path, string? old = null, bool directory = false) =>
        new(change, path, old, directory);

    [Fact]
    public void FileEventsRescanTheirFolderShallowly()
    {
        var plan = ChangePlanner.Plan([
            E(FileSystemChange.Created, @"C:\lib\2024\a.jpg"),
            E(FileSystemChange.Changed, @"C:\lib\2024\b.jpg"),
            E(FileSystemChange.Deleted, @"C:\lib\c.jpg"),
        ], Root);
        Assert.Equal([new RescanTarget("", false), new RescanTarget("2024", false)], plan);
    }

    [Fact]
    public void NewOrRenamedDirectoriesAreRescannedDeeplyAndCoalesce()
    {
        var plan = ChangePlanner.Plan([
            E(FileSystemChange.Renamed, @"C:\lib\2024", @"C:\lib\old", directory: true),
            E(FileSystemChange.Created, @"C:\lib\2024\May\x.jpg"),
        ], Root);
        Assert.Equal([new RescanTarget("", false), new RescanTarget("2024", true)], plan);
    }

    [Fact]
    public void RenamesRescanBothFolders()
    {
        var plan = ChangePlanner.Plan([E(FileSystemChange.Renamed, @"C:\lib\b\x.jpg", @"C:\lib\a\x.jpg")], Root);
        Assert.Equal([new RescanTarget("a", false), new RescanTarget("b", false)], plan);
    }

    [Fact]
    public void OverflowForcesAFullRescanAndHiddenOrForeignPathsAreIgnored()
    {
        Assert.Equal([new RescanTarget("", true)], ChangePlanner.Plan([E(FileSystemChange.Overflow, Root)], Root));
        Assert.Empty(ChangePlanner.Plan([
            E(FileSystemChange.Created, @"C:\lib\.cache\a.jpg"),
            E(FileSystemChange.Created, @"C:\library-other\a.jpg"),
        ], Root));
        // Paths compare without case on Windows.
        Assert.Equal([new RescanTarget("x", false)], ChangePlanner.Plan([E(FileSystemChange.Changed, @"c:\LIB\x\a.jpg")], Root));
    }
}

public sealed class ExifDateTests
{
    [Fact]
    public void OffsetsAreHonoredAndGarbageRejected()
    {
        var utc = TimeZoneInfo.Utc;
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_715_934_600), ExifDate.Parse("2024:05:17 10:30:00", "+02:00", utc));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_715_941_800), ExifDate.Parse("2024:05:17 10:30:00", null, utc));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_715_958_000), ExifDate.Parse("2024:05:17 10:30:00", "-04:30", utc));
        Assert.Null(ExifDate.Parse("0000:00:00 00:00:00", null));
        Assert.Null(ExifDate.Parse("garbage", null));
        Assert.Null(ExifDate.Parse("2024:13:40 10:30:00", null));
    }
}

public sealed class QuickHashAndListerTests
{
    [Fact]
    public void QuickHashSeesHeadTailAndSize()
    {
        using var folder = new TemporaryDirectory();
        var big = new string('a', 300_000);
        var first = folder.Write("1.bin", big);
        var same = folder.Write("2.bin", big);
        var otherTail = folder.Write("3.bin", big[..^1] + "b");
        var otherMiddle = folder.Write("4.bin", big[..150_000] + "b" + big[150_001..]);
        var small = folder.Write("5.bin", "hello");
        Assert.Equal(QuickHash.Compute(first), QuickHash.Compute(same));
        Assert.NotEqual(QuickHash.Compute(first), QuickHash.Compute(otherTail));
        // The middle is not sampled: identical head, tail and size hash the same (documented limitation).
        Assert.Equal(QuickHash.Compute(first), QuickHash.Compute(otherMiddle));
        Assert.Equal(16, QuickHash.Compute(small)?.Length);
        Assert.Null(QuickHash.Compute(folder.Full("missing")));
    }

    [Fact]
    public void ListerSkipsHiddenItemsAndNonMedia()
    {
        using var folder = new TemporaryDirectory();
        folder.Write("a.jpg");
        folder.Write("b.txt");
        folder.Write(".dot.jpg");
        folder.Write(".dot_dir/c.jpg");
        File.SetAttributes(folder.Write("hidden.jpg"), FileAttributes.Hidden);
        folder.Write("sub/e.jpg");
        var listing = new DirectoryLister(new ManagedDirectoryReader()).List(folder.Path, 1, "");
        Assert.Equal(["a.jpg"], listing.Files.Select(f => f.Name));
        Assert.Equal(["sub"], listing.Subdirectories);
        Assert.Equal(1, listing.Files[0].Size);
        Assert.Equal("public.jpeg", listing.Files[0].TypeIdentifier);
    }

    [Fact]
    public void FolderListingGivesRequestedKindsInNaturalOrder()
    {
        using var folder = new TemporaryDirectory();
        foreach (var name in new[] { "IMG_10.jpg", "IMG_2.heic", "img_1.PNG", "clip.mov", ".hidden.jpg", "notes.txt", "sub/deep.jpg" })
        {
            folder.Write(name);
        }
        var lister = new DirectoryLister(new ManagedDirectoryReader());
        Assert.Equal(["img_1.PNG", "IMG_2.heic", "IMG_10.jpg"],
            FolderListing.Files(folder.Path, new HashSet<MediaKind> { MediaKind.Photo }, lister).Select(f => f.Name));
        Assert.Equal(["clip.mov", "img_1.PNG", "IMG_2.heic", "IMG_10.jpg"],
            FolderListing.Files(folder.Path, AssetQuery.AllKinds, lister).Select(f => f.Name));
    }
}
