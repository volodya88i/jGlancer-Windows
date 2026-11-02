using Avalonia.Threading;
using JGlancer.App.Library;
using JGlancer.App.Services;
using JGlancer.Core.Model;
using JGlancer.Core.Playback;
using JGlancer.Core.Scanning;

namespace JGlancer.App.Shell;

/// <summary>
/// Opening files and folders from outside the app (macOS D-63, D-65; W11): Explorer's Open With, the context menu, a
/// second launch. A library file opens as from the grid; any other file opens with the media files of its folder of
/// its kind around it, as stand-ins (nothing is written to the library). A folder joins the library and shows.
/// Opened for a file at launch, the window is the viewer alone: the library does not load, Esc or closing the window
/// quits, "‹ Library" loads the library and turns the window into the whole app.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The window shows a viewer only and the library has not loaded (launched for a file).</summary>
    public bool IsViewerOnly { get; private set; }

    /// <summary>The kind of the viewer shown, or "none" (developer launch log).</summary>
    public string ViewerKind => _viewer is not null ? "photo" : _mediaViewer?.Model?.Kind.ToString().ToLowerInvariant() ?? "none";

    /// <summary>"‹ Library" or a folder left viewer-only mode: the app loads the library now.</summary>
    public event EventHandler? LibraryNeeded;

    /// <summary>The folder of the outside file shown (its viewer offers Add Folder).</summary>
    private string? _outsideFolder;

    /// <summary>Starts in viewer-only mode for <paramref name="paths"/> (the library is not started).</summary>
    public void BeginViewerOnly() => IsViewerOnly = true;

    /// <summary>Opens the first media file in a viewer, or shows a folder (added to the library first).</summary>
    public async Task OpenPathsAsync(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (_model is not { } model)
        {
            return;
        }
        if (paths.FirstOrDefault(Directory.Exists) is { } folder && !paths.Any(p => File.Exists(p) && MediaClassifier.Classify(p) is not null))
        {
            CloseViewer();
            LeaveViewerOnly();
            await model.AddFoldersAsync([folder], quietIfPresent: true);
            if (await model.Store.LocationAsync(folder, isDirectory: true) is { FolderId: { } folderId })
            {
                model.SelectFolder(folderId);
            }
            return;
        }
        if (paths.FirstOrDefault(p => File.Exists(p) && MediaClassifier.Classify(p) is not null) is not { } file)
        {
            LeaveViewerOnly();
            return;
        }
        CloseViewer(reveal: false);
        if (await model.Store.LocationAsync(file, isDirectory: false) is { AssetId: { } assetId })
        {
            _outsideFolder = null;
            await OpenAsync(assetId);
            return;
        }
        await OpenOutsideAsync(model, file);
    }

    /// <summary>A file outside the library: its folder's files of its kind, natural order, as stand-ins.</summary>
    private async Task OpenOutsideAsync(LibraryModel model, string file)
    {
        var kind = MediaClassifier.Classify(file)!.Kind;
        var siblings = await Task.Run(() => FolderSiblings.Of(file, kind));
        var standIns = model.Services.StandIns;
        var ids = siblings.Select(standIns.Add).OfType<long>().ToList();
        var index = standIns.Add(file) is { } fileId ? ids.IndexOf(fileId) : 0;
        if (ids.Count == 0 || IsViewerOpen)
        {
            return;
        }
        if (IsQuickPreviewOpen)
        {
            _quickPreview!.Dismiss();
        }
        _preview?.Suspend();
        _outsideFolder = Path.GetDirectoryName(file);
        if (kind == MediaKind.Photo)
        {
            OpenPhotos(model, ids, Math.Max(0, index), standIns.Get(ids[Math.Max(0, index)]));
        }
        else
        {
            var items = ids.Select(id => standIns.Get(id)!).Select(asset => PlaybackItem.From(asset)).ToList();
            OpenMedia(model, kind, items, Math.Max(0, index));
        }
        ShowAddFolder();
    }

    /// <summary>An outside file's viewer offers its folder to the library ("This folder isn't in the library").</summary>
    private void ShowAddFolder()
    {
        var bar = _viewer?.TopBar ?? _mediaViewer?.TopBar;
        if (bar is null)
        {
            return;
        }
        bar.AddFolderVisible = _outsideFolder is not null;
        bar.AddFolderRequested -= OnAddOutsideFolder;
        bar.AddFolderRequested += OnAddOutsideFolder;
    }

    private async void OnAddOutsideFolder(object? sender, EventArgs e)
    {
        if (_model is not { } model || _outsideFolder is not { } folder)
        {
            return;
        }
        _outsideFolder = null;
        ShowAddFolder();
        // The library loads behind the viewer (macOS D-65: without leaving viewer-only mode).
        if (IsViewerOnly && !_libraryStarted)
        {
            _libraryStarted = true;
            LibraryNeeded?.Invoke(this, EventArgs.Empty);
        }
        await model.AddFoldersAsync([folder], quietIfPresent: true);
    }

    private bool _libraryStarted;

    /// <summary>Esc in a viewer: back to the library, or quit when the window is the viewer alone.</summary>
    private void OnViewerClose()
    {
        if (IsViewerOnly)
        {
            Close();
            return;
        }
        CloseViewer();
    }

    /// <summary>"‹ Library": the library, loaded first when the window was the viewer alone.</summary>
    private void ShowLibrary()
    {
        LeaveViewerOnly();
        CloseViewer();
    }

    private void LeaveViewerOnly()
    {
        if (!IsViewerOnly)
        {
            return;
        }
        IsViewerOnly = false;
        if (!_libraryStarted)
        {
            _libraryStarted = true;
            LibraryNeeded?.Invoke(this, EventArgs.Empty);
        }
        Dispatcher.UIThread.Post(() => Library.IsVisible = !IsViewerOpen);
    }
}
