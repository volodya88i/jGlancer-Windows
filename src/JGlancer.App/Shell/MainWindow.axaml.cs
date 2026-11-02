using Avalonia;
using Avalonia.Controls;
using JGlancer.App.Library;
using JGlancer.App.Preview;
using Avalonia.Threading;
using JGlancer.App.Viewer;
using JGlancer.App.Viewer.Media;
using JGlancer.Core.Playback;
using JGlancer.Core;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.Shell;

/// <summary>The one main window: title bar with menus, the library, and the viewer filling the space below the bar.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The previews are disposed in OnClosed, with the window.")]
internal sealed partial class MainWindow : Window, Actions.IActionHost
{
    private LibraryModel? _model;
    private PhotoViewerView? _viewer;
    private MediaViewerView? _mediaViewer;

    public MainWindow()
    {
        InitializeComponent();
        Title = AppIdentity.Name;
        JGWindowChrome.Apply(this);
        TitleBar.AddFolderRequested += async (_, _) => await Library.AddFolderAsync();
    }

    public MainWindow(LibraryModel model)
        : this()
    {
        _model = model;
        DataContext = model;
        TitleBar.Model = model;
        TitleBar.Primary = model;
        FlagStar.Library = model;
        model.OpenRequested += async (_, id) => await OpenAsync(id);
        // A stack folded or unfolded: the preview's frame timeline follows.
        model.RefsChanged += async (_, _) =>
        {
            if (_preview is { } preview)
            {
                await preview.SequencesChangedAsync();
            }
        };
        _preview = new PreviewModel(model.Services, model.Selection)
        {
            // A folded sequence scrubs and plays in the previews (W-86).
            SequenceFrames = id => ActiveModel is { } active && active.IsFolded(id) ? active.SequenceOf(id)?.Frames : null,
        };
        Library.AttachPreview(_preview);
        model.QuickPreviewKey += (_, e) => e.Handled = HandleQuickPreviewKey(e.Key);
        TitleBar.QuickPreviewRequested += (_, _) => ToggleQuickPreview();
        TitleBar.IsQuickPreviewOpen = () => IsQuickPreviewOpen;
        _actions = new Actions.ActionPerformer(model.Services, this);
        AttachActions();
        AttachFileOperations(model);
        AttachSplit(model);
        AttachNowPlaying(model);
        ScanProgress.Model = model;
        model.ConfirmWholeDrive = drive => Support.ConfirmDialog.ShowAsync(this, Core.Localization.L.T("Add the whole drive “{0}”?", drive),
            Core.Localization.L.T("Scanning a whole drive takes long and brings in every photo, video and song on it, system and program folders included."),
            Core.Localization.L.T("Add Drive"));
        ShowOnboarding();
    }

    /// <summary>First launch: the choice of a look in place of the library, until it is made or skipped (macOS).</summary>
    private void ShowOnboarding()
    {
        var store = Settings.SettingsStore.Current;
        if (store.Settings.HasChosenAppearance)
        {
            return;
        }
        var onboarding = new Settings.OnboardingView();
        Body.Children.Add(onboarding);
        void OnChanged(object? sender, (Core.Settings.AppSettings Old, Core.Settings.AppSettings New) change)
        {
            if (change.New.HasChosenAppearance)
            {
                store.Changed -= OnChanged;
                Body.Children.Remove(onboarding);
                Library.Focus();
            }
        }
        store.Changed += OnChanged;
    }

    private readonly PreviewModel? _preview;
    private QuickPreviewWindow? _quickPreview;

    public LibraryView LibraryView => Library;

    /// <summary>The previews' model (developer hooks).</summary>
    public PreviewModel? Preview => _preview;

    /// <summary>Quick Preview, once it was opened (developer hooks).</summary>
    public QuickPreviewWindow? QuickPreview => _quickPreview;

    public bool IsQuickPreviewOpen => _quickPreview?.IsVisible == true;

    // MARK: Quick Preview

    /// <summary>Space toggles Quick Preview, Esc closes it, Enter pauses, F11 full screen (Space and Esc leave full
    /// screen first); false when the key is not for Quick Preview (macOS AppModel.handle).</summary>
    private bool HandleQuickPreviewKey(QuickPreviewKey key)
    {
        var open = IsQuickPreviewOpen;
        switch (key)
        {
            case QuickPreviewKey.Space when open && _quickPreview!.IsFullScreen:
                _quickPreview.ToggleFullScreen();
                return true;
            case QuickPreviewKey.Space:
                ToggleQuickPreview();
                return true;
            case QuickPreviewKey.Escape when open:
                _quickPreview!.Dismiss();
                return true;
            case QuickPreviewKey.Enter when open:
                _preview?.TogglePlay();
                return true;
            case QuickPreviewKey.FullScreen when open:
                _quickPreview!.ToggleFullScreen();
                return true;
            default:
                return false;
        }
    }

    public void ToggleQuickPreview()
    {
        if (IsQuickPreviewOpen)
        {
            _quickPreview!.Dismiss();
        }
        else
        {
            OpenQuickPreview();
        }
    }

    public void OpenQuickPreview()
    {
        if (_preview is null || _model is null || ActiveModel?.Selection.Previewed is null || IsViewerOpen)
        {
            return;
        }
        if (_quickPreview is null)
        {
            var window = new QuickPreviewWindow(_preview);
            window.Dismissed += (_, _) => QuickPreviewClosed();
            window.OpenRequested += async (_, _) =>
            {
                // The file shown opens in the viewer, as Enter on the grid would.
                if (ActiveModel?.Selection.Previewed is { } previewed)
                {
                    window.Dismiss();
                    await OpenAsync(previewed);
                }
            };
            window.ArrowPressed += (_, key) => Library.LibraryGrid.MoveSelection(key switch
            {
                Avalonia.Input.Key.Left => GridMove.Left,
                Avalonia.Input.Key.Right => GridMove.Right,
                Avalonia.Input.Key.Up => GridMove.Up,
                _ => GridMove.Down,
            });
            _quickPreview = window;
        }
        _quickPreview.Place(_model.State.QuickPreviewFrame, this);
        _preview.SetQuickPreviewOpen(true);
        _quickPreview.Show(this);
    }

    private void QuickPreviewClosed()
    {
        _preview?.SetQuickPreviewOpen(false);
        if (_quickPreview?.LastFrame is { } frame && _model is { } model && frame != model.State.QuickPreviewFrame)
        {
            model.UpdateState(s => s.QuickPreviewFrame = frame);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_quickPreview is { } window)
        {
            window.AllowsClose = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // An open player stops with the window: the app may stay in the notification area (Space helper), silent.
        CloseViewer(reveal: false);
        _quickPreview?.Close();
        _preview?.Dispose();
        _nowPlaying?.Dispose();
        _mediaKeys?.Dispose();
        _stems?.Dispose();
        base.OnClosed(e);
    }

    /// <summary>The open photo viewer, if any (developer hooks).</summary>
    public PhotoViewerView? Viewer => _viewer;

    /// <summary>The open video or audio viewer, if any (developer hooks).</summary>
    public MediaViewerView? MediaViewer => _mediaViewer;

    /// <summary>The largest screen in device pixels: screen-size decodes fit it, so moving or resizing the window never
    /// needs a new decode.</summary>
    private PixelSize LargestScreen
    {
        get
        {
            var largest = new PixelSize(2560, 1440);
            var area = 0L;
            foreach (var screen in Screens.All)
            {
                var size = screen.Bounds.Size;
                if ((long)size.Width * size.Height > area)
                {
                    area = (long)size.Width * size.Height;
                    largest = size;
                }
            }
            return largest;
        }
    }

    /// <summary>
    /// Opens the viewer on a library file (macOS openViewer, D-09): a photo pages through the photos the pane shows (its
    /// folder, with the subfolders' files only while View â–¸ Include Subfolders is on; W-67), a video or audio file plays
    /// through the files of its kind there, in the library's sort. A file outside that (a search) gets its own folder.
    /// </summary>
    /// <param name="from">The pane's model it was opened in (split view); the active one by default.</param>
    public async Task OpenAsync(long assetId, LibraryModel? from = null)
    {
        if ((from ?? ActiveModel) is not { } model || IsViewerOpen)
        {
            return;
        }
        var assets = await model.Store.AssetsAsync([assetId]);
        if (assets.Count == 0)
        {
            return;
        }
        var asset = assets[0];
        var ids = await PagingListAsync(model, asset);
        if (IsViewerOpen)
        {
            return;
        }
        // The previews stand aside while a viewer is open (macOS viewerChanged).
        if (IsQuickPreviewOpen)
        {
            _quickPreview!.Dismiss();
        }
        _preview?.Suspend();
        if (asset.Kind == MediaKind.Photo)
        {
            OpenPhotos(model, ids, ids.IndexOf(assetId), asset);
        }
        else
        {
            var rows = (await model.Store.AssetsAsync(ids)).ToDictionary(row => row.Id);
            var items = ids.Where(rows.ContainsKey).Select(id => PlaybackItem.From(rows[id])).ToList();
            OpenMedia(model, asset.Kind, items, Math.Max(0, items.FindIndex(item => item.AssetId == assetId)));
        }
    }

    /// <summary>The files a viewer pages through from <paramref name="asset"/>: of its kind, as the pane shows them.</summary>
    private static async Task<List<long>> PagingListAsync(LibraryModel model, Asset asset)
    {
        var query = new AssetQuery
        {
            // From an album: its files of that kind in its order; else the folder shown, as the pane shows it.
            Scope = model.State.AlbumId is { } album ? QueryScope.Album(album)
                : QueryScope.InFolder(model.State.FolderId ?? asset.FolderId, model.State.IncludeSubfolders),
            Kinds = new HashSet<MediaKind> { asset.Kind },
            Sort = model.State.Sort,
            Ascending = model.State.Ascending,
        };
        var ids = (await model.Store.AssetRefsAsync(query)).Select(r => r.Id).ToList();
        if (!ids.Contains(asset.Id))
        {
            // Not among them (found by a search elsewhere, or opened from Explorer while an album it is not in is
            // shown): the files of its own folder, in the library's sort.
            ids = [.. (await model.Store.AssetRefsAsync(query with { Scope = QueryScope.InFolder(asset.FolderId, includeSubfolders: false) })).Select(r => r.Id)];
        }
        if (ids.Count == 0)
        {
            ids.Add(asset.Id);
        }
        return ids;
    }

    private void OpenPhotos(LibraryModel model, List<long> ids, int index, Asset? opened = null)
    {
        var viewer = new PhotoViewerView
        {
            Model = new PhotoViewerModel(model.Services, ids, Math.Max(0, index), LargestScreen, opened),
        };
        viewer.CloseRequested += (_, _) => OnViewerClose();
        viewer.LibraryRequested += (_, _) => ShowLibrary();
        _viewer = viewer;
        Body.Children.Add(viewer);
        Library.IsVisible = false;
    }

    private void OpenMedia(LibraryModel model, MediaKind kind, List<PlaybackItem> items, int index)
    {
        if (!JGlancer.Mpv.MpvPlayer.IsAvailable || items.Count == 0)
        {
            // Without libmpv (scripts\deps.ps1 not run) there is no player.
            _preview?.Resume();
            return;
        }
        var viewer = new MediaViewerView
        {
            Model = new MediaViewerModel(model.Services, kind, items, index, model.Services.PlaybackPreferences),
        };
        viewer.CloseRequested += (_, _) => OnViewerClose();
        viewer.LibraryRequested += (_, _) => ShowLibrary();
        viewer.MiniPlayerRequested += (_, media) => HandToMiniPlayer(media);
        viewer.FullScreenRequested += (_, _) => ToggleFullScreen();
        viewer.MessageRequested += (_, message) => _fileOps?.ShowMessage(message);
        _mediaViewer = viewer;
        Body.Children.Add(viewer);
        Library.IsVisible = false;
        UpdateMediaKeys();
        if (Developer.DeveloperOptions.Current.HdrCheck is { } hdrLog && kind == MediaKind.Video)
        {
            _ = viewer.RunHdrCheckAsync(hdrLog);
        }
        if (Developer.DeveloperOptions.Current.SwitchCheck is { } switchLog && kind == MediaKind.Video)
        {
            _ = viewer.RunSwitchCheckAsync(switchLog);
        }
        if (Developer.DeveloperOptions.Current.TracksCheck is { } tracksLog && kind == MediaKind.Video)
        {
            _ = viewer.RunTracksCheckAsync(tracksLog);
        }
    }

    /// <summary>Full screen hides the title bar (W-04); the viewer's Esc leaves it first.</summary>
    private void ToggleFullScreen()
    {
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            var fullScreen = WindowState == WindowState.FullScreen;
            TitleBar.IsVisible = !fullScreen;
            NowPlayingHost.IsVisible = !fullScreen;
            if (_mediaViewer is not null)
            {
                _mediaViewer.IsFullScreen = fullScreen;
            }
        }
    }

    /// <summary>Back to the library with the file shown last selected and scrolled into view.</summary>
    /// <param name="reveal">False when another file opens right away (from Explorer): nothing to show in between.</param>
    public void CloseViewer(bool reveal = true)
    {
        long? shown = null;
        if (_viewer is { } viewer)
        {
            shown = viewer.Model?.CurrentId;
            viewer.Model?.Dispose();
            viewer.Model = null;
            Body.Children.Remove(viewer);
            _viewer = null;
        }
        shown = CloseCompare() ?? shown;
        if (_mediaViewer is { } media && media.Model is { } mediaModel)
        {
            shown = mediaModel.Current?.AssetId;
            mediaModel.Playback.Stop();
            var released = media.PlayerReleased;
            Body.Children.Remove(media);
            media.Model = null;
            _mediaViewer = null;
            UpdateMediaKeys();
            // The player goes once the video surface freed its render context (mpv's rule).
            _ = released.ContinueWith(_ => Dispatcher.UIThread.Post(mediaModel.Dispose), TaskScheduler.Default);
            if (WindowState == WindowState.FullScreen)
            {
                WindowState = WindowState.Normal;
            }
        }
        Library.IsVisible = !IsViewerOnly;
        _outsideFolder = null;
        if (reveal && shown is > 0 and var id)
        {
            Library.Reveal(id);
        }
        _preview?.Resume();
    }
}