using Avalonia.Controls;
using Avalonia.Input;
using JGlancer.App.Actions;
using JGlancer.App.FileOps;
using JGlancer.App.Library;
using JGlancer.App.Services;
using JGlancer.Core.Actions;
using JGlancer.Core.FileOps;

namespace JGlancer.App.Shell;

/// <summary>
/// File operations in the window (macOS AppModel's FileOperationsModel wiring): the toast, Undo and Redo, F2 and
/// Delete, renaming on a tile or by a pattern, and the viewer after its file went to the Recycle Bin.
/// </summary>
internal sealed partial class MainWindow
{
    private FileOperationsModel? _fileOps;

    FileOperationsModel IActionHost.FileOps => _fileOps!;

    /// <summary>The file operations (developer hooks, the menu bar).</summary>
    public FileOperationsModel? FileOps => _fileOps;

    private void AttachFileOperations(LibraryModel model)
    {
        var fileOps = new FileOperationsModel(model.Store, model.Services.RecentFolders) { Owner = this };
        _fileOps = fileOps;
        Toasts.Model = fileOps;
        Library.RenameFile = fileOps.RenameAsync;
        Library.ShowMessage = fileOps.ShowMessage;
        Library.DropFiles = (paths, folder, copying) => fileOps.TransferAsync(paths, folder, copying);
        Library.ExportFiles = paths => fileOps.TransferAsync(paths, null, copying: true);
        Library.CreateFolder = (parent, name) => fileOps.CreateFolderAsync(parent, name);
        Library.TrashFolder = path => fileOps.TrashAsync([path]);
        Library.TrashFiles = paths => fileOps.TrashAsync(paths);
        model.UndoableChanged += (_, change) => fileOps.Register(change);
        model.FlagUndoReplaced += (_, swap) => fileOps.Replace(swap.Last, swap.Change);
        // mpv holds its file open without delete sharing: the players let go before Windows is asked to change it.
        fileOps.FilesChanging += (_, paths) =>
        {
            _preview?.Release(paths);
            if (_mediaViewer?.Model?.Current is { } playing && paths.Any(p => Core.Store.LibraryPaths.IsSameOrInside(playing.Path, p)))
            {
                _mediaViewer.Model.Playback.Stop();
            }
        };
        TitleBar.AttachUndo(fileOps);
        AttachStems(model, fileOps);
        TitleBar.SettingsRequested += (_, _) => OpenSettings();
        AddHandler(KeyDownEvent, OnFileOperationKey, Avalonia.Interactivity.RoutingStrategies.Bubble);
        AttachFileDrop();
    }

    /// <summary>Ctrl+, for Settings; Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z (text fields keep their own).</summary>
    private async void OnFileOperationKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _fileOps is not { } fileOps)
        {
            return;
        }
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (control && !shift && e.Key == Key.OemComma)
        {
            e.Handled = true;
            OpenSettings();
        }
        else if (control && e.Key == Key.Z && !shift)
        {
            e.Handled = true;
            await fileOps.UndoAsync();
        }
        else if (control && (e.Key == Key.Y || (e.Key == Key.Z && shift)))
        {
            e.Handled = true;
            await fileOps.RedoAsync();
        }
    }

    private Settings.SettingsWindow? _settings;

    /// <summary>Settings (Ctrl+,), one window; shown again where it was left.</summary>
    public Settings.SettingsWindow? OpenSettings(Settings.SettingsSection? section = null)
    {
        if (_model is null || _actions is null)
        {
            return null;
        }
        if (_settings is { } open)
        {
            if (section is { } chosen)
            {
                open.Show(chosen);
            }
            open.Activate();
            return open;
        }
        var window = new Settings.SettingsWindow(_model, _actions.OpenWith, section ?? Settings.SettingsSection.General);
        window.Closed += (_, _) => _settings = null;
        _settings = window;
        window.Show(this);
        return window;
    }

    void IActionHost.BeginRename(long assetId) => Library.BeginRename(assetId);

    async Task IActionHost.ShowBatchRenameAsync(ActionTarget target)
    {
        if (_model is null || _fileOps is null)
        {
            return;
        }
        var assets = await _model.Services.AssetsAsync(target.AssetIds);
        var order = target.AssetIds.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index);
        var files = assets.OrderBy(a => order.GetValueOrDefault(a.Id))
            .Select(a => new BatchRenamePattern.File(a.Path, a.CapturedAt ?? a.ModifiedAt))
            .ToList();
        if (files.Count > 1 && await BatchRenameWindow.AskAsync(this, files) is { Count: > 0 } items)
        {
            await _fileOps.RenameAllAsync(items);
        }
    }

    /// <summary>The viewer's file went to the Recycle Bin: the next one shows, or the viewer closes when none is left.</summary>
    void IActionHost.ViewerFileRemoved()
    {
        if (_viewer?.Model is { } photos && !photos.RemoveCurrent())
        {
            CloseViewer();
        }
        else if (_mediaViewer?.Model is { } media && !media.Playback.RemoveCurrent())
        {
            CloseViewer();
        }
    }
}
