using System.Diagnostics;
using JGlancer.Core.Scanning;
using JGlancer.Core.Tests.Support;
using JGlancer.Platform.Windows;

namespace JGlancer.Platform.Tests;

public sealed class DirectoryReaderTests
{
    private readonly WindowsDirectoryReader _reader = new();

    [Fact]
    public void EntriesMatchTheManagedReaderAndCarryFileIds()
    {
        using var folder = new TemporaryDirectory();
        folder.Write("a.jpg", "12345");
        folder.Write("sub/b.jpg");
        File.SetAttributes(folder.Write("hidden.jpg"), FileAttributes.Hidden);
        var windows = _reader.Read(folder.Path).OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
        var managed = new ManagedDirectoryReader().Read(folder.Path).OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
        Assert.Equal(managed.Select(e => (e.Name, e.IsDirectory, e.IsHidden, e.Size, e.ModifiedAt)), windows.Select(e => (e.Name, e.IsDirectory, e.IsHidden, e.Size, e.ModifiedAt)));
        Assert.All(windows, e => Assert.NotNull(e.FileId));
        Assert.Equal(3, windows.Select(e => e.FileId).Distinct().Count());
    }

    [Fact]
    public void FileIdSurvivesRenameAndMove()
    {
        using var folder = new TemporaryDirectory();
        folder.Write("a.jpg");
        var before = _reader.Read(folder.Path).Single(e => e.Name == "a.jpg").FileId;
        folder.Move("a.jpg", "sub/renamed.jpg");
        Assert.Equal(before, _reader.Read(folder.Full("sub")).Single().FileId);
    }

    [Fact]
    public void JunctionsAreReportedAsLinksAndCanonicalPathsResolveThem()
    {
        using var folder = new TemporaryDirectory();
        folder.Write("Real Folder/a.jpg");
        Run("cmd.exe", "/c", "mklink", "/J", folder.Full("junction"), folder.Full("Real Folder"));
        var junction = _reader.Read(folder.Path).Single(e => e.Name == "junction");
        Assert.True(junction.IsLink);
        Assert.Equal(folder.Full("Real Folder"), _reader.Canonical(folder.Full("junction")), ignoreCase: true);
        // The canonical form also restores the case stored on disk.
        Assert.EndsWith(@"\Real Folder", _reader.Canonical(folder.Full("real folder")), StringComparison.Ordinal);
    }

    [Fact]
    public void LongPathsAreListed()
    {
        using var folder = new TemporaryDirectory();
        var deep = string.Join('/', Enumerable.Repeat(new string('d', 60), 5));
        folder.Write(deep + "/long.jpg");
        Assert.True(folder.Full(deep).Length > 260);
        Assert.Equal("long.jpg", _reader.Read(folder.Full(deep)).Single().Name);
    }

    [Fact]
    public void MissingDirectoriesAndVolumesBehave()
    {
        Assert.Throws<DirectoryNotFoundException>(() => _reader.Read(Path.Combine(Path.GetTempPath(), "jGlancer-missing-" + Guid.NewGuid())));
        Assert.Matches("^[0-9A-F]{8}$", _reader.VolumeId(Path.GetTempPath()));
    }

    private static void Run(string file, params string[] arguments)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
