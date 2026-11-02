using JGlancer.Core.Model;
using JGlancer.Core.Scanning;
using JGlancer.Core.Tests.Support;

namespace JGlancer.Core.Tests.Scanning;

/// <summary>The files a viewer pages through for a file opened from Explorer (macOS FinderPreview listing).</summary>
public sealed class FolderSiblingsTests
{
    [Fact]
    public void SameKindInNaturalOrderWithoutDotFiles()
    {
        Assert.Equal(["a2.jpg", "a10.JPG", "b.png"],
            FolderSiblings.Choose(["a10.JPG", "b.png", "song.mp3", ".hidden.jpg", "a2.jpg", "notes.txt"], MediaKind.Photo));
        Assert.Equal(["song.mp3"], FolderSiblings.Choose(["a.jpg", "song.mp3"], MediaKind.Audio));
    }

    [Fact]
    public void AFolderIsListedShallowAndHiddenFilesStayOut()
    {
        using var folder = new TemporaryDirectory();
        foreach (var name in new[] { "b.jpg", "a.jpg", "c.mp4", "sub/d.jpg", "hidden.jpg" })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(folder.Full(name))!);
            File.WriteAllBytes(folder.Full(name), [0]);
        }
        File.SetAttributes(folder.Full("hidden.jpg"), FileAttributes.Hidden);
        Assert.Equal([folder.Full("a.jpg"), folder.Full("b.jpg")], FolderSiblings.Of(folder.Full("b.jpg"), MediaKind.Photo));
        // A hidden file opened on purpose is still there, where it sorts.
        Assert.Equal([folder.Full("a.jpg"), folder.Full("b.jpg"), folder.Full("hidden.jpg")], FolderSiblings.Of(folder.Full("hidden.jpg"), MediaKind.Photo));
        Assert.Equal([folder.Full("c.mp4")], FolderSiblings.Of(folder.Full("c.mp4"), MediaKind.Video));
    }
}
