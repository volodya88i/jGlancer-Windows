using JGlancer.Core.FileOps;
using JGlancer.Core.Tests.Support;

namespace JGlancer.Core.Tests.FileOps;

public sealed class BatchRenameTests
{
    private static readonly DateTimeOffset Date = new(2024, 7, 3, 12, 0, 0, TimeSpan.Zero);

    private static List<BatchRenamePattern.File> Files(params string[] names) =>
        [.. names.Select(name => new BatchRenamePattern.File(@"C:\f\" + name, Date))];

    private static readonly Func<string, IReadOnlySet<string>> Nothing = _ => new HashSet<string>();

    [Fact]
    public void TemplatesKeepExtensionsAndPadCounters()
    {
        var batch = Files("IMG_1.jpg", "IMG_2.heic", "clip.mov");
        Assert.Equal(["Trip 1.jpg", "Trip 2.heic", "Trip 3.mov"], new BatchRenamePattern { Template = "Trip {n}" }.Names(batch));
        Assert.Equal(["2024-07-03_09.jpg", "2024-07-03_10.heic", "2024-07-03_11.mov"], new BatchRenamePattern { Template = "{date}_{n}", Start = 9 }.Names(batch));
        Assert.Equal(["IMG_1-001.jpg", "IMG_2-002.heic", "clip-003.mov"], new BatchRenamePattern { Template = "{name}-{n}", Digits = 3 }.Names(batch));
        Assert.Equal(["IMG_1.jpg", "IMG_2.heic", "clip.mov"], new BatchRenamePattern().Names(batch));
    }

    [Fact]
    public void ANewExtensionReplacesEveryExtensionAndKeepsNames()
    {
        var batch = Files("IMG_1.jpeg", "IMG_2.JPEG", "notes");
        Assert.Equal(["IMG_1.jpg", "IMG_2.jpg", "notes.jpg"], new BatchRenamePattern { NewExtension = " .jpg " }.Names(batch));
        Assert.Equal(["IMG_1.jpeg", "IMG_2.JPEG", "notes"], new BatchRenamePattern { NewExtension = "..." }.Names(batch));
        var pattern = new BatchRenamePattern { NewExtension = "a/b" };
        Assert.All(BatchRenamePattern.Problems(batch, pattern.Names(batch), Nothing), p => Assert.Equal(BatchRenamePattern.Problem.InvalidCharacters, p));
    }

    [Fact]
    public void ProblemsAreFoundPerFile()
    {
        var batch = Files("a.jpg", "b.jpg", "c.jpg");
        IReadOnlySet<string> Existing(string _) => new HashSet<string> { "a.jpg", "b.jpg", "c.jpg", "Taken.jpg" };
        Assert.Equal([BatchRenamePattern.Problem.Empty, BatchRenamePattern.Problem.InvalidCharacters, BatchRenamePattern.Problem.Taken],
            BatchRenamePattern.Problems(batch, ["  .jpg", "x:y.jpg", "taken.jpg"], Existing));
        Assert.Equal([BatchRenamePattern.Problem.Duplicate, BatchRenamePattern.Problem.Duplicate, null],
            BatchRenamePattern.Problems(batch, ["d.jpg", "D.jpg", "b.jpg"], Existing));
        // Trading places with another file of the batch is fine.
        Assert.Equal([null, null, null], BatchRenamePattern.Problems(batch, ["b.jpg", "a.jpg", "c.jpg"], Existing));
    }

    [Fact]
    public async Task TheRunnerSwapsShiftsAndUndoesInOneRecord()
    {
        using var directory = new TemporaryDirectory();
        var disk = new TestFileSystem(directory.Path);
        foreach (var name in new[] { "1.jpg", "2.jpg", "3.jpg", "other.jpg" })
        {
            directory.Write("f/" + name, "content of " + name);
        }
        var folder = directory.Full("f");
        string In(string name) => Path.Combine(folder, name);
        using var runner = new FileOperationRunner(disk);
        // A shift where the last name is taken by a file outside the batch: 3 keeps its name, then 2 and 1 fail too.
        var blocked = await runner.PerformAsync(new FileOperation.RenameAll([(In("1.jpg"), "2.jpg"), (In("2.jpg"), "3.jpg"), (In("3.jpg"), "other.jpg")]));
        Assert.Equal(0, blocked.Completed);
        Assert.Equal(3, blocked.Failures.Count);
        Assert.Equal(["1.jpg", "2.jpg", "3.jpg", "other.jpg"], TestFileSystem.Names(folder));
        // Without the blocked one, the shift works.
        var shifted = await runner.PerformAsync(new FileOperation.RenameAll([(In("1.jpg"), "2.jpg"), (In("2.jpg"), "4.jpg")]));
        Assert.Equal(2, shifted.Completed);
        Assert.Equal("content of 1.jpg", TestFileSystem.Contents(In("2.jpg")));
        Assert.Equal("content of 2.jpg", TestFileSystem.Contents(In("4.jpg")));
        Assert.DoesNotContain(TestFileSystem.Names(folder), n => n.StartsWith(".jglancer", StringComparison.Ordinal));
        var undone = await runner.PerformAsync(shifted.Undo);
        Assert.Empty(undone.Failures);
        Assert.Equal("content of 1.jpg", TestFileSystem.Contents(In("1.jpg")));
        Assert.Equal("content of 2.jpg", TestFileSystem.Contents(In("2.jpg")));
        // A swap.
        var swapped = await runner.PerformAsync(new FileOperation.RenameAll([(In("1.jpg"), "2.jpg"), (In("2.jpg"), "1.jpg")]));
        Assert.Equal(2, swapped.Completed);
        Assert.Equal("content of 2.jpg", TestFileSystem.Contents(In("1.jpg")));
        Assert.Equal(["1.jpg", "2.jpg", "3.jpg", "other.jpg"], TestFileSystem.Names(folder));
    }

    [Theory]
    [InlineData("ok.jpg", true)]
    [InlineData(".gitignore", true)]
    [InlineData("a<b.jpg", false)]
    [InlineData("name.", false)]
    [InlineData("name ", false)]
    [InlineData("NUL.txt", false)]
    [InlineData("com1", false)]
    [InlineData("tab\there", false)]
    public void WindowsNameRules(string name, bool valid) => Assert.Equal(valid, FileNames.IsValid(name));
}
