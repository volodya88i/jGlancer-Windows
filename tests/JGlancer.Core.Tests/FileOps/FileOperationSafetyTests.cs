using JGlancer.Core.FileOps;
using JGlancer.Core.Store;
using JGlancer.Core.Tests.Store;
using JGlancer.Core.Tests.Support;

namespace JGlancer.Core.Tests.FileOps;

/// <summary>
/// The real file system inside a temporary folder, with a private Recycle Bin, a simulated second volume and injected
/// failures (macOS TestFileSystem; ARCHITECTURE §20: mandatory data-safety tests).
/// </summary>
internal sealed class TestFileSystem : IFileSystem
{
    private int _moves;

    public TestFileSystem(string root)
    {
        RecycleBin = Path.Combine(root, "$recycle");
        OtherVolume = Path.Combine(root, "volumeB");
        Directory.CreateDirectory(RecycleBin);
        Directory.CreateDirectory(OtherVolume);
    }

    public string RecycleBin { get; }

    /// <summary>Paths under this folder are on "another volume".</summary>
    public string OtherVolume { get; }

    public int? FailMoveNumber { get; set; }

    public bool CorruptCopies { get; set; }

    /// <summary>Deleting anything under this folder fails (a source that cannot be removed).</summary>
    public string? FailRemoveUnder { get; set; }

    public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    public string? Volume(string path) => path.StartsWith(OtherVolume, StringComparison.OrdinalIgnoreCase) ? "B" : "A";

    public void Move(string source, string destination)
    {
        if (Interlocked.Increment(ref _moves) == FailMoveNumber)
        {
            throw new IOException("injected move failure");
        }
        if (Directory.Exists(source))
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination);
        }
    }

    public void Copy(string source, string destination)
    {
        File.Copy(source, destination);
        if (CorruptCopies)
        {
            File.AppendAllText(destination, "!");
        }
    }

    public void Remove(string path)
    {
        if (FailRemoveUnder is { } folder && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("injected remove failure");
        }
        File.Delete(path);
    }

    public string Trash(string path)
    {
        var target = Path.Combine(RecycleBin, Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(path));
        Move(path, target);
        return target;
    }

    public void Restore(string trashed, string destination) => Move(trashed, destination);

    public void CreateDirectory(string path)
    {
        if (Exists(path))
        {
            throw new IOException("exists");
        }
        Directory.CreateDirectory(path);
    }

    public long? Size(string path) => File.Exists(path) ? new FileInfo(path).Length : null;

    public byte[]? QuickHash(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

    /// <summary>Names in <paramref name="folder"/>, temporary ones included, sorted.</summary>
    public static List<string> Names(string folder) =>
        [.. Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    public static string? Contents(string path) => File.Exists(path) ? File.ReadAllText(path) : null;
}

public sealed class FileOperationSafetyTests : IDisposable
{
    private readonly TemporaryDirectory _directory = new();
    private readonly TestFileSystem _disk;
    private readonly string _source;
    private readonly string _destination;

    public FileOperationSafetyTests()
    {
        _disk = new TestFileSystem(_directory.Path);
        _source = _directory.Full("src");
        _destination = _directory.Full("dst");
        foreach (var name in new[] { "a.jpg", "b.jpg", "c.jpg" })
        {
            _directory.Write("src/" + name, "content of " + name);
        }
        Directory.CreateDirectory(_destination);
    }

    public void Dispose() => _directory.Dispose();

    private List<string> Files(params string[] names) => [.. names.Select(name => Path.Combine(_source, name))];

    private static List<string> Names(string folder) => TestFileSystem.Names(folder);

    [Fact]
    public async Task MoveThenUndoThenRedo()
    {
        using var runner = new FileOperationRunner(_disk);
        var moved = await runner.PerformAsync(new FileOperation.Move(Files("a.jpg", "b.jpg"), _destination));
        Assert.Equal(2, moved.Completed);
        Assert.Equal(["a.jpg", "b.jpg"], Names(_destination));
        Assert.Equal(["c.jpg"], Names(_source));
        var undone = await runner.PerformAsync(moved.Undo);
        Assert.Empty(undone.Failures);
        Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], Names(_source));
        Assert.Empty(Names(_destination));
        await runner.PerformAsync(undone.Undo);
        Assert.Equal(["a.jpg", "b.jpg"], Names(_destination));
        Assert.Equal("content of a.jpg", TestFileSystem.Contents(Path.Combine(_destination, "a.jpg")));
    }

    [Fact]
    public async Task AFailureMidBatchKeepsEveryFileAndUndoesTheRest()
    {
        _disk.FailMoveNumber = 2;
        using var runner = new FileOperationRunner(_disk);
        var result = await runner.PerformAsync(new FileOperation.Move(Files("a.jpg", "b.jpg", "c.jpg"), _destination));
        Assert.Equal(2, result.Completed);
        Assert.Equal(Files("b.jpg"), result.Failures);
        Assert.Equal(["b.jpg"], Names(_source));
        Assert.Equal(["a.jpg", "c.jpg"], Names(_destination));
        _disk.FailMoveNumber = null;
        await runner.PerformAsync(result.Undo);
        Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], Names(_source));
        Assert.Empty(Names(_destination));
    }

    [Fact]
    public async Task AMoveToAnotherVolumeVerifiesBeforeDeletingTheSource()
    {
        using var runner = new FileOperationRunner(_disk);
        var result = await runner.PerformAsync(new FileOperation.Move(Files("a.jpg"), _disk.OtherVolume));
        Assert.Equal(1, result.Completed);
        Assert.Equal(["a.jpg"], Names(_disk.OtherVolume));
        Assert.Equal(["b.jpg", "c.jpg"], Names(_source));
        await runner.PerformAsync(result.Undo);
        Assert.Equal("content of a.jpg", TestFileSystem.Contents(Path.Combine(_source, "a.jpg")));
        Assert.Empty(Names(_disk.OtherVolume));
    }

    [Fact]
    public async Task ABadCopyNeverCostsTheOriginal()
    {
        _disk.CorruptCopies = true;
        using var runner = new FileOperationRunner(_disk);
        var moved = await runner.PerformAsync(new FileOperation.Move(Files("a.jpg"), _disk.OtherVolume));
        Assert.Equal(Files("a.jpg"), moved.Failures);
        var copied = await runner.PerformAsync(new FileOperation.Copy(Files("b.jpg"), _destination));
        Assert.Equal(Files("b.jpg"), copied.Failures);
        Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], Names(_source));
        Assert.Empty(Names(_disk.OtherVolume));
        Assert.Empty(Names(_destination));
        Assert.Equal("content of a.jpg", TestFileSystem.Contents(Path.Combine(_source, "a.jpg")));
    }

    [Fact]
    public async Task WhenTheSourceCannotBeDeletedTheMoveIsTakenBack()
    {
        _disk.FailRemoveUnder = _source;
        using var runner = new FileOperationRunner(_disk);
        var result = await runner.PerformAsync(new FileOperation.Move(Files("a.jpg"), _disk.OtherVolume));
        Assert.Equal(Files("a.jpg"), result.Failures);
        Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], Names(_source));
        Assert.Empty(Names(_disk.OtherVolume));
    }

    [Fact]
    public async Task NameCollisionsFollowThePolicy()
    {
        _directory.Write("dst/a.jpg", "old a");
        _directory.Write("dst/a 2.jpg", "old a 2");
        using var runner = new FileOperationRunner(_disk);
        var skipped = await runner.PerformAsync(new FileOperation.Copy(Files("a.jpg"), _destination), ConflictPolicy.Skip);
        Assert.Equal(Files("a.jpg"), skipped.Skipped);
        var both = await runner.PerformAsync(new FileOperation.Copy(Files("a.jpg"), _destination), ConflictPolicy.KeepBoth);
        Assert.Equal(["a 3.jpg"], both.Results.Select(Path.GetFileName));
        var replaced = await runner.PerformAsync(new FileOperation.Move(Files("a.jpg"), _destination), ConflictPolicy.Replace);
        Assert.Equal(1, replaced.Completed);
        Assert.Equal("content of a.jpg", TestFileSystem.Contents(Path.Combine(_destination, "a.jpg")));
        Assert.Single(Names(_disk.RecycleBin));
        // Undo brings the moved file back and the replaced one out of the Recycle Bin.
        await runner.PerformAsync(replaced.Undo);
        Assert.Equal("content of a.jpg", TestFileSystem.Contents(Path.Combine(_source, "a.jpg")));
        Assert.Equal("old a", TestFileSystem.Contents(Path.Combine(_destination, "a.jpg")));
        Assert.Empty(Names(_disk.RecycleBin));
    }

    [Fact]
    public async Task TheRecycleBinRoundTripAndAnOccupiedPlace()
    {
        using var runner = new FileOperationRunner(_disk);
        var trashed = await runner.PerformAsync(new FileOperation.Trash(Files("a.jpg", "b.jpg")));
        Assert.Equal(2, trashed.Completed);
        Assert.Equal(["c.jpg"], Names(_source));
        _directory.Write("src/b.jpg", "a new b");
        await runner.PerformAsync(trashed.Undo);
        Assert.Equal(["a.jpg", "b 2.jpg", "b.jpg", "c.jpg"], Names(_source));
        Assert.Equal("content of b.jpg", TestFileSystem.Contents(Path.Combine(_source, "b 2.jpg")));
        Assert.Empty(Names(_disk.RecycleBin));
    }

    [Fact]
    public async Task RenameRulesAndUndo()
    {
        using var runner = new FileOperationRunner(_disk);
        var a = Files("a.jpg")[0];
        Assert.Single((await runner.PerformAsync(new FileOperation.Rename(a, "b.jpg"))).Failures);
        Assert.Single((await runner.PerformAsync(new FileOperation.Rename(a, "x/y.jpg"))).Failures);
        Assert.Single((await runner.PerformAsync(new FileOperation.Rename(a, "  "))).Failures);
        Assert.Single((await runner.PerformAsync(new FileOperation.Rename(a, "CON.jpg"))).Failures);
        Assert.Single((await runner.PerformAsync(new FileOperation.Rename(a, "dot."))).Failures);
        var renamed = await runner.PerformAsync(new FileOperation.Rename(a, "Holiday.jpg"));
        Assert.Equal(["Holiday.jpg", "b.jpg", "c.jpg"], Names(_source));
        var caseOnly = await runner.PerformAsync(new FileOperation.Rename(Files("b.jpg")[0], "B.jpg"));
        Assert.Equal(1, caseOnly.Completed);
        Assert.Contains("B.jpg", Names(_source));
        await runner.PerformAsync(renamed.Undo);
        Assert.Contains("a.jpg", Names(_source));
    }

    [Fact]
    public async Task CopyUndoSendsOnlyTheCopiesToTheRecycleBinAndLeavesNoTemporaryFiles()
    {
        using var runner = new FileOperationRunner(_disk);
        var copied = await runner.PerformAsync(new FileOperation.Copy(Files("a.jpg", "b.jpg"), _destination));
        Assert.Equal(["a.jpg", "b.jpg"], Names(_destination));
        await runner.PerformAsync(copied.Undo);
        Assert.Empty(Names(_destination));
        Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], Names(_source));
        Assert.Equal(2, Names(_disk.RecycleBin).Count);
    }

    [Fact]
    public async Task CancellingStopsBetweenItems()
    {
        using var runner = new FileOperationRunner(_disk);
        using var cancel = new CancellationTokenSource();
        var result = await runner.PerformAsync(new FileOperation.Move(Files("a.jpg", "b.jpg", "c.jpg"), _destination),
            progress: p =>
            {
                if (p.Done == 1)
                {
                    cancel.Cancel();
                }
            }, cancellation: cancel.Token);
        Assert.True(result.WasCancelled);
        Assert.Equal(1, result.Completed);
        Assert.Equal(["a.jpg"], Names(_destination));
        Assert.Equal(["b.jpg", "c.jpg"], Names(_source));
    }

    [Fact]
    public async Task ANewFolderGetsAFreeNameAndUndoSendsItToTheRecycleBin()
    {
        Directory.CreateDirectory(Path.Combine(_destination, "New folder"));
        using var runner = new FileOperationRunner(_disk);
        var created = await runner.PerformAsync(new FileOperation.CreateFolder(_destination, "New folder"));
        Assert.Equal(["New folder 2"], created.Results.Select(Path.GetFileName));
        await runner.PerformAsync(created.Undo);
        Assert.Equal(["New folder"], Names(_destination));
    }

    [Fact]
    public async Task MovedTrashedAndRestoredFilesKeepTheirAssetIds()
    {
        using var store = LibraryStore.InMemory();
        var root = await store.AddRootAsync(_source);
        await store.ScanAsync(root.Id, [StoreTestSupport.File("a.jpg"), StoreTestSupport.File("b.jpg"), StoreTestSupport.File("c.jpg")], ["sub"]);
        var sub = Path.Combine(_source, "sub");
        Directory.CreateDirectory(sub);
        var id = (await store.LocationAsync(Files("a.jpg")[0], isDirectory: false))!.AssetId;
        Assert.NotNull(id);
        using var runner = new FileOperationRunner(_disk, new LibraryFileIndex(store));
        var moved = await runner.PerformAsync(new FileOperation.Move(Files("a.jpg"), sub));
        var movedPath = Path.Combine(sub, "a.jpg");
        Assert.Equal(id, (await store.LocationAsync(movedPath, isDirectory: false))?.AssetId);
        var trashed = await runner.PerformAsync(new FileOperation.Trash(moved.Results));
        Assert.Null((await store.LocationAsync(movedPath, isDirectory: false))?.AssetId);
        await runner.PerformAsync(trashed.Undo);
        Assert.Equal(id, (await store.LocationAsync(movedPath, isDirectory: false))?.AssetId);
    }
}
