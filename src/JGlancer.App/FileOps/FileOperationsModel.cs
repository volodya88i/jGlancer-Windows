using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using JGlancer.App.Support;
using JGlancer.Core.FileOps;
using JGlancer.Core.Localization;
using JGlancer.Core.Store;

namespace JGlancer.App.FileOps;

/// <summary>A message at the bottom of the window (macOS FileOperationsModel.Toast).</summary>
/// <param name="Progress">0…1 while a long batch runs.</param>
/// <param name="ActionLabel">A button of its own ("Show in Explorer"), run by <paramref name="Action"/>.</param>
internal sealed record Toast(string Message, bool CanUndo = false, double? Progress = null, string? ActionLabel = null, Action? Action = null);

/// <summary>
/// An undoable change outside the file system (albums): running it undoes (or redoes) the change and returns its
/// inverse (macOS CustomUndo).
/// </summary>
internal sealed record CustomUndo(string Name, Func<Task<CustomUndo?>> Run);

/// <summary>
/// The app's file operations (macOS FileOperationsModel, FEATURES §7): runs them on the <see cref="FileOperationRunner"/>,
/// asks about name conflicts, shows progress and results as toasts with Undo, keeps the undo / redo history
/// (Ctrl+Z / Ctrl+Y) and the recent destination folders. Nothing is ever deleted for good from here: the Recycle Bin
/// takes it, and that is undoable. UI thread only.
/// </summary>
internal sealed class FileOperationsModel : IDisposable
{
    /// <summary>Batches of at least this many files show their progress.</summary>
    public const int ProgressThreshold = 10;

    public const int RecentLimit = 8;

    private readonly FileOperationRunner _runner;
    private readonly JsonFile _recentFile;
    // UndoRecord (files) or CustomUndo (albums).
    private readonly List<object> _undo = [];
    private readonly List<object> _redo = [];
    private readonly List<string> _recent;
    private readonly DispatcherTimer _dismiss;
    private CancellationTokenSource? _running;

    public FileOperationsModel(LibraryStore store, JsonFile recentFolders)
    {
        ArgumentNullException.ThrowIfNull(recentFolders);
        _runner = new FileOperationRunner(new Platform.Windows.WindowsFileSystem(), new LibraryFileIndex(store));
        _recentFile = recentFolders;
        _recent = ReadRecent(recentFolders.Read());
        _dismiss = new DispatcherTimer(TimeSpan.FromSeconds(Design.JGMetrics.ToastSeconds), DispatcherPriority.Background, (_, _) => DismissToast());
        _dismiss.Stop();
    }

    /// <summary>The window: owner of dialogs and pickers.</summary>
    public Window? Owner { get; set; }

    public Toast? Toast { get; private set; }

    public bool IsRunning => _running is not null;

    public IReadOnlyList<string> RecentFolders => _recent;

    public bool CanUndo => _undo.Count > 0 && !IsRunning;

    public bool CanRedo => _redo.Count > 0 && !IsRunning;

    /// <summary>"Move", "Rename", … for Edit ▸ Undo ….</summary>
    public string UndoTitle => _undo.Count > 0 ? EntryName(_undo[^1]) : "";

    public string RedoTitle => _redo.Count > 0 ? EntryName(_redo[^1]) : "";

    private static string EntryName(object entry) => entry is CustomUndo custom ? custom.Name : ActionName(((UndoRecord)entry).Kind);

    /// <summary>Puts <paramref name="change"/> in place of <paramref name="last"/> when that is still the newest step (a flag chord); else records it.</summary>
    public void Replace(CustomUndo last, CustomUndo change)
    {
        if (_undo.Count > 0 && ReferenceEquals(_undo[^1], last))
        {
            _undo.RemoveAt(_undo.Count - 1);
        }
        Register(change);
    }

    /// <summary>Records an undoable change that is not a file operation (albums); clears the redo history.</summary>
    public void Register(CustomUndo change)
    {
        ArgumentNullException.ThrowIfNull(change);
        _undo.Add(change);
        _redo.Clear();
    }

    /// <summary>
    /// Records files something else made (a stem split, W-85) as one undoable step — undoing moves them to the Recycle
    /// Bin — and shows <paramref name="toast"/> with Undo.
    /// </summary>
    public void RegisterCreated(UndoKind kind, IReadOnlyList<string> created, Toast toast)
    {
        ArgumentNullException.ThrowIfNull(created);
        ArgumentNullException.ThrowIfNull(toast);
        if (created.Count > 0)
        {
            _undo.Add(new UndoRecord(kind, [.. created.Select(path => new FileStep.Trash(path))]));
            _redo.Clear();
        }
        Show(toast with { CanUndo = created.Count > 0 });
    }

    /// <summary>The toast changed (shown, progress, gone).</summary>
    public event EventHandler? ToastChanged;

    /// <summary>
    /// Files are about to move, be renamed or go to the Recycle Bin: whoever holds them open (the players) lets go,
    /// or Windows refuses the change.
    /// </summary>
    public event EventHandler<IReadOnlyList<string>>? FilesChanging;

    /// <summary>Files moved, renamed, restored or sent to the Recycle Bin (after every operation, undo and redo).</summary>
    public event EventHandler<FileOperationResult>? FilesChanged;

    // MARK: Operations

    /// <summary>Move to… / Copy to…: <paramref name="folder"/>, or one chosen in the folder picker.</summary>
    public async Task TransferAsync(IReadOnlyList<string> paths, string? folder, bool copying)
    {
        if (paths.Count == 0 || IsRunning)
        {
            return;
        }
        folder ??= await ChooseFolderAsync(copying);
        if (folder is null)
        {
            return;
        }
        var conflicts = paths.Count(path =>
        {
            var target = Path.Combine(folder, Path.GetFileName(path));
            return (File.Exists(target) || Directory.Exists(target)) && !FileNames.Same(target, path);
        });
        var policy = ConflictPolicy.KeepBoth;
        if (conflicts > 0)
        {
            if (await AskConflictPolicyAsync(conflicts, folder) is not { } chosen)
            {
                return;
            }
            policy = chosen;
        }
        Remember(folder);
        await RunAsync(copying ? new FileOperation.Copy(paths, folder) : new FileOperation.Move(paths, folder), paths.Count, policy);
    }

    /// <summary>A new folder inside <paramref name="parent"/> (a free name next to a taken one); undo sends it to the Recycle Bin.</summary>
    public Task<FileOperationResult?> CreateFolderAsync(string parent, string name) =>
        RunAsync(new FileOperation.CreateFolder(parent, name), 1, ConflictPolicy.KeepBoth);

    /// <summary>To the Recycle Bin (undoable); null when nothing ran.</summary>
    public Task<FileOperationResult?> TrashAsync(IReadOnlyList<string> paths) =>
        paths.Count == 0 ? Task.FromResult<FileOperationResult?>(null) : RunAsync(new FileOperation.Trash(paths), paths.Count, ConflictPolicy.KeepBoth);

    /// <summary>Renames one file; returns a message when the name cannot be used (the field shows it and stays).</summary>
    public async Task<string?> RenameAsync(string path, string name)
    {
        if (IsRunning)
        {
            return null;
        }
        FilesChanging?.Invoke(this, [path]);
        var result = await _runner.PerformAsync(new FileOperation.Rename(path, name));
        if (result.Failures.Count > 0)
        {
            var target = Path.Combine(Path.GetDirectoryName(path) ?? "", name.Trim());
            return FileNames.IsValid(name.Trim()) && (File.Exists(target) || Directory.Exists(target))
                ? L.T("That name is taken.")
                : L.T("That name can't be used.");
        }
        Finish(result);
        return null;
    }

    /// <summary>Renames many files at once (one undo); names that cannot be used fail alone.</summary>
    public Task RenameAllAsync(IReadOnlyList<(string Source, string Name)> items) =>
        items.Count == 0 ? Task.CompletedTask : RunAsync(new FileOperation.RenameAll(items), items.Count, ConflictPolicy.KeepBoth);

    public Task UndoAsync() => RunRecordAsync(_undo, _redo, L.T("Undone"));

    public Task RedoAsync() => RunRecordAsync(_redo, _undo, L.T("Redone"));

    /// <summary>Stops a running batch between two files (what is done stays done, and undoable).</summary>
    public void Stop() => _running?.Cancel();

    public void DismissToast()
    {
        _dismiss.Stop();
        if (Toast is not null && Toast.Progress is null)
        {
            Toast = null;
            ToastChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>A one-line message (a rename that could not be done).</summary>
    public void ShowMessage(string message) => Show(new Toast(message));

    // MARK: Running

    private async Task<FileOperationResult?> RunAsync(FileOperation operation, int count, ConflictPolicy policy)
    {
        if (IsRunning)
        {
            return null;
        }
        if (Sources(operation) is { Count: > 0 } sources)
        {
            FilesChanging?.Invoke(this, sources);
        }
        var cancel = new CancellationTokenSource();
        _running = cancel;
        var kind = operation.Kind;
        if (count >= ProgressThreshold)
        {
            Show(new Toast(ProgressMessage(kind, 0, count), Progress: 0));
        }
        try
        {
            var result = await _runner.PerformAsync(operation, policy,
                progress => Dispatcher.UIThread.Post(() => ShowProgress(kind, progress)), cancel.Token);
            _running = null;
            Finish(result);
            return result;
        }
        finally
        {
            _running = null;
            cancel.Dispose();
        }
    }

    /// <summary>Files an operation changes in place (copies leave theirs alone).</summary>
    private static IReadOnlyList<string> Sources(FileOperation operation) => operation switch
    {
        FileOperation.Move move => move.Sources,
        FileOperation.Rename rename => [rename.Source],
        FileOperation.RenameAll all => [.. all.Items.Select(item => item.Source)],
        FileOperation.Trash trash => trash.Sources,
        _ => [],
    };

    private async Task RunRecordAsync(List<object> from, List<object> to, string message)
    {
        if (IsRunning || from.Count == 0)
        {
            return;
        }
        var entry = from[^1];
        from.RemoveAt(from.Count - 1);
        if (entry is CustomUndo custom)
        {
            if (await custom.Run() is { } inverse)
            {
                to.Add(inverse);
            }
            Show(new Toast(message));
            return;
        }
        var record = (UndoRecord)entry;
        var cancel = new CancellationTokenSource();
        _running = cancel;
        try
        {
            var result = await _runner.PerformAsync(record, cancellation: cancel.Token);
            if (!result.Undo.IsEmpty)
            {
                to.Add(result.Undo);
            }
            FilesChanged?.Invoke(this, result);
            Show(new Toast(result.Failures.Count > 0 ? message + " · " + L.T("Failed: {0}", result.Failures.Count) : message));
        }
        finally
        {
            _running = null;
            cancel.Dispose();
        }
    }

    private void Finish(FileOperationResult result)
    {
        if (!result.Undo.IsEmpty)
        {
            _undo.Add(result.Undo);
            _redo.Clear();
        }
        FilesChanged?.Invoke(this, result);
        var message = DoneMessage(result.Undo.Kind, result.Completed);
        if (result.Failures.Count > 0)
        {
            message += " · " + L.T("Failed: {0}", result.Failures.Count);
        }
        if (result.Skipped.Count > 0 && result.Undo.Kind != UndoKind.Rename)
        {
            message += " · " + L.T("Skipped: {0}", result.Skipped.Count);
        }
        Show(new Toast(message, CanUndo: !result.Undo.IsEmpty));
    }

    private void ShowProgress(UndoKind kind, FileProgress progress)
    {
        if (IsRunning && progress.Total >= ProgressThreshold)
        {
            Toast = new Toast(ProgressMessage(kind, progress.Done, progress.Total), Progress: (double)progress.Done / Math.Max(progress.Total, 1));
            ToastChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Show(Toast toast)
    {
        Toast = toast;
        _dismiss.Stop();
        if (toast.Progress is null)
        {
            _dismiss.Start();
        }
        ToastChanged?.Invoke(this, EventArgs.Empty);
    }

    // MARK: Folders and conflicts

    private void Remember(string folder)
    {
        _recent.RemoveAll(existing => FileNames.Same(existing, folder));
        _recent.Insert(0, folder);
        if (_recent.Count > RecentLimit)
        {
            _recent.RemoveRange(RecentLimit, _recent.Count - RecentLimit);
        }
        _recentFile.Write(JsonSerializer.Serialize(_recent, RecentFoldersJson.Default.ListString));
    }

    private static List<string> ReadRecent(string? json)
    {
        try
        {
            var read = json is null ? null : JsonSerializer.Deserialize(json, RecentFoldersJson.Default.ListString);
            return [.. (read ?? []).Where(Directory.Exists).Take(RecentLimit)];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<string?> ChooseFolderAsync(bool copying)
    {
        if (Owner is null)
        {
            return null;
        }
        var start = _recent.Count > 0 ? await Owner.StorageProvider.TryGetFolderFromPathAsync(new Uri(_recent[0] + Path.DirectorySeparatorChar)) : null;
        var folders = await Owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = copying ? L.T("Copy to…") : L.T("Move to…"),
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    /// <summary>Keep Both (default), Replace (the existing files go to the Recycle Bin), Skip, or Cancel.</summary>
    private async Task<ConflictPolicy?> AskConflictPolicyAsync(int count, string folder)
    {
        if (Owner is null)
        {
            return ConflictPolicy.KeepBoth;
        }
        return await ChoiceDialog<ConflictPolicy>.ShowAsync(Owner,
            L.T("{0} files already exist in “{1}”.", count, Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar))),
            L.T("Replaced files go to the Recycle Bin."),
            [(L.T("Keep Both"), ConflictPolicy.KeepBoth), (L.T("Replace"), ConflictPolicy.Replace), (L.T("Skip"), ConflictPolicy.Skip)]);
    }

    // MARK: Wording

    public static string ActionName(UndoKind kind) => kind switch
    {
        UndoKind.Move => L.T("Move"),
        UndoKind.Copy => L.T("Copy"),
        UndoKind.Rename => L.T("Rename"),
        UndoKind.Trash => L.T("Move to Recycle Bin"),
        UndoKind.SplitStems => L.T("Split into Stems"),
        _ => L.T("New Folder"),
    };

    private static string DoneMessage(UndoKind kind, int count) => kind switch
    {
        UndoKind.Move => L.T("Moved: {0}", count),
        UndoKind.Copy => L.T("Copied: {0}", count),
        UndoKind.Rename => L.T("Renamed"),
        UndoKind.Trash => L.T("Moved to Recycle Bin: {0}", count),
        _ => L.T("Folder created"),
    };

    private static string ProgressMessage(UndoKind kind, int done, int total) => kind switch
    {
        UndoKind.Copy => L.T("Copying {0} of {1}…", done, total),
        UndoKind.Trash => L.T("Moving to Recycle Bin {0} of {1}…", done, total),
        _ => L.T("Moving {0} of {1}…", done, total),
    };

    public void Dispose()
    {
        _running?.Cancel();
        _runner.Dispose();
    }
}

/// <summary>Source-generated JSON for the recent folders (NativeAOT).</summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(List<string>))]
internal sealed partial class RecentFoldersJson : System.Text.Json.Serialization.JsonSerializerContext;
