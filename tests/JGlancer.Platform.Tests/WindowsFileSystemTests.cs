using JGlancer.Core.FileOps;
using JGlancer.Core.Tests.Support;
using JGlancer.Platform.Windows;

namespace JGlancer.Platform.Tests;

/// <summary>
/// The real Recycle Bin, with a file made for the test in a temporary folder: it goes there and comes back, and the
/// Recycle Bin keeps no entry of it.
/// </summary>
public sealed class WindowsFileSystemTests
{
    [Fact]
    public async Task AFileGoesToTheRecycleBinAndComesBackWithoutLeavingAnEntry()
    {
        using var folder = new TemporaryDirectory();
        var path = folder.Write("jGlancer-undo-check.txt", "made by a jGlancer test; it should not stay here");
        using var runner = new FileOperationRunner(new WindowsFileSystem());
        var trashed = await runner.PerformAsync(new FileOperation.Trash([path]), cancellation: TestContext.Current.CancellationToken);
        Assert.Equal(1, trashed.Completed);
        Assert.False(File.Exists(path));
        var item = Assert.IsType<FileStep.Restore>(Assert.Single(trashed.Undo.Steps)).Trashed;
        Assert.True(File.Exists(item), "the item is found in the Recycle Bin");
        var restored = await runner.PerformAsync(trashed.Undo, cancellation: TestContext.Current.CancellationToken);
        Assert.Empty(restored.Failures);
        Assert.Equal("made by a jGlancer test; it should not stay here", File.ReadAllText(path));
        Assert.False(File.Exists(item));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(item)!, "$I" + Path.GetFileName(item)[2..])), "no broken entry stays listed");
    }

    [Fact]
    public async Task AFolderGoesToTheRecycleBinWithItsFilesAndComesBack()
    {
        using var folder = new TemporaryDirectory();
        folder.Write("jGlancer folder check/inside.txt", "made by a jGlancer test");
        var path = folder.Full("jGlancer folder check");
        using var runner = new FileOperationRunner(new WindowsFileSystem());
        var trashed = await runner.PerformAsync(new FileOperation.Trash([path]), cancellation: TestContext.Current.CancellationToken);
        Assert.Equal(1, trashed.Completed);
        Assert.False(Directory.Exists(path));
        var restored = await runner.PerformAsync(trashed.Undo, cancellation: TestContext.Current.CancellationToken);
        Assert.Empty(restored.Failures);
        Assert.Equal("made by a jGlancer test", File.ReadAllText(Path.Combine(path, "inside.txt")));
    }

    [Fact]
    public void VolumesTellDrivesApart()
    {
        using var folder = new TemporaryDirectory();
        var disk = new WindowsFileSystem();
        Assert.Equal(disk.Volume(folder.Path), disk.Volume(Path.Combine(folder.Path, "not yet there.jpg")));
        Assert.NotNull(disk.Volume(folder.Path));
    }
}
