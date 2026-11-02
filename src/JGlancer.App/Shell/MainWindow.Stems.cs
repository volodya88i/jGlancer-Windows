using JGlancer.App.FileOps;
using JGlancer.App.Library;
using JGlancer.App.Settings;
using JGlancer.App.Stems;
using JGlancer.Core.Stems;

namespace JGlancer.App.Shell;

/// <summary>
/// Split into Stems… (W-85): the split window, the model download when it is missing, then the songs go to the
/// window's queue (<see cref="StemJobs"/>) with the card at the bottom.
/// </summary>
internal sealed partial class MainWindow
{
    private StemJobs? _stems;

    private void AttachStems(LibraryModel model, FileOperationsModel fileOps)
    {
        _stems = new StemJobs(model.Services.Paths.Models, fileOps);
        StemProgress.Jobs = _stems;
    }

    /// <summary>`--stem-check`: splits <paramref name="song"/> as Split would with 4 stems, logs what it made, quits.</summary>
    public async Task RunStemCheckAsync(string song, string log)
    {
        if (_stems is not { } jobs)
        {
            return;
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var done = new TaskCompletionSource();
        jobs.Changed += (_, _) =>
        {
            if (jobs.Current is null)
            {
                done.TrySetResult();
            }
        };
        var folder = StemNaming.DefaultFolder(song, Directory.Exists);
        jobs.Enqueue([new StemJob(song, folder, FolderIsNew: true, StemChoice.Default)]);
        await done.Task;
        var lines = new List<string> { $"song {song}", $"seconds {clock.Elapsed.TotalSeconds:0.0}", $"toast {_fileOps?.Toast?.Message}", $"undo {_fileOps?.UndoTitle}" };
        lines.AddRange(Directory.Exists(folder)
            ? Directory.GetFiles(folder).Select(f => $"file {Path.GetFileName(f)} {new FileInfo(f).Length}")
            : ["no folder"]);
        await File.WriteAllLinesAsync(log, lines);
        Close();
    }

    void Actions.IActionHost.SplitIntoStems(IReadOnlyList<string> songs) => _ = SplitIntoStemsAsync(songs);

    private async Task SplitIntoStemsAsync(IReadOnlyList<string> songs)
    {
        if (_stems is not { } jobs || songs.Count == 0)
        {
            return;
        }
        var settings = SettingsStore.Current.Settings;
        var all = StemModelInfo.Shown(settings.StemMode).ToHashSet();
        var remembered = new StemChoice(settings.StemMode, settings.StemTicks is { Count: > 0 } ticks ? ticks.ToHashSet() : all);
        if (!remembered.CanSplit)
        {
            remembered = new StemChoice(settings.StemMode, all);
        }
        if (await StemSplitWindow.AskAsync(this, songs, remembered) is not { } request)
        {
            return;
        }
        SettingsStore.Current.Update(s => s with { StemMode = request.Choice.Mode, StemTicks = [.. request.Choice.Ticked] });
        var full = SettingsStore.Current.Settings.StemModelFull;
        if (!await StemModelWindow.EnsureAsync(this, StemModelInfo.For(request.Choice.Mode, full), jobs.ModelFolder))
        {
            return;
        }
        // Folders of this batch count as taken before they exist (two songs of the same name).
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Taken(string path) => Directory.Exists(path) || File.Exists(path) || reserved.Contains(path);
        var batch = new List<StemJob>();
        foreach (var song in songs)
        {
            if (request.Folder is { } chosen && songs.Count == 1)
            {
                batch.Add(new StemJob(song, chosen, FolderIsNew: false, request.Choice, full));
                continue;
            }
            var folder = StemNaming.DefaultFolder(request.Folder is { } parent ? Path.Combine(parent, Path.GetFileName(song)) : song, Taken);
            reserved.Add(folder);
            batch.Add(new StemJob(song, folder, FolderIsNew: true, request.Choice, full));
        }
        jobs.Enqueue(batch);
    }
}
