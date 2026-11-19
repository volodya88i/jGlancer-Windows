using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace JGlancer.App.Library;

/// <summary>
/// One pane of the library: top bar, grid with its empty states, status bar, renaming in place, tiles dragged out
/// and in. Bound to its model (the main one, or the split view's follower); folders dropped from Explorer join the
/// library, folders dragged from the sidebar show here.
/// </summary>
internal sealed partial class BrowserPane : UserControl
{
    /// <summary>A folder row dragged from the sidebar (its id): the pane it is dropped on shows it.</summary>
    public static readonly DataFormat<string> FolderFormat = DataFormat.CreateStringApplicationFormat("jglancer-folder");

    public BrowserPane()
    {
        InitializeComponent();
        AddFolderButton.Click += (_, _) => AddFolderRequested?.Invoke(this, EventArgs.Empty);
        DropZone.AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        DropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DropZone.AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropHighlight.IsVisible = false);
        DropZone.AddHandler(DragDrop.DropEvent, OnDrop);
        TileGrid.DragRequested += OnGridDragRequested;
        FileList.DragRequested += OnGridDragRequested;
        AttachAlbumReorder();
        GridScroller.TemplateApplied += (_, e) =>
        {
            _scrollBar = e.NameScope.Find<Avalonia.Controls.Primitives.ScrollBar>("PART_VerticalScrollBar");
            UpdateStatusLine();
        };
        ListScroller.TemplateApplied += (_, e) =>
        {
            _listScrollBar = e.NameScope.Find<Avalonia.Controls.Primitives.ScrollBar>("PART_VerticalScrollBar");
            UpdateStatusLine();
        };
        // Settings → Appearance → Decor can hide the status bar; it shows only with folders.
        Settings.SettingsStore.Current.Changed += (_, _) => UpdateStatusLine();
        DataContextChanged += (_, _) =>
        {
            if (Model is { } model)
            {
                model.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(LibraryModel.HasRoots))
                    {
                        UpdateStatusLine();
                    }
                };
                model.StateChanged += (_, _) => UpdateViewMode();
            }
            UpdateStatusLine();
        };
        // Any press or the keyboard in the pane makes it the active one (split view).
        AddHandler(PointerPressedEvent, (_, _) => Activated?.Invoke(this, EventArgs.Empty), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(GotFocusEvent, (_, _) => Activated?.Invoke(this, EventArgs.Empty), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>The empty state's Add Folder… (the window shows the picker).</summary>
    public event EventHandler? AddFolderRequested;

    /// <summary>The pane was clicked or took the keyboard.</summary>
    public event EventHandler? Activated;

    public LibraryModel? Model => DataContext as LibraryModel;

    /// <summary>The grid (benchmarks and snapshots drive it).</summary>
    public Grid.LibraryGrid Grid => TileGrid;

    /// <summary>The list (View ▸ as List).</summary>
    public List.LibraryList List => FileList;

    /// <summary>The view showing the files now: the grid or the list.</summary>
    public Control FilesView => Model?.ShowsList == true ? FileList : TileGrid;

    /// <summary>Moves the selection as an arrow key does, in the grid or the list (Quick Preview's arrows).</summary>
    public void MoveSelection(Core.Model.GridMove move)
    {
        if (Model?.ShowsList == true)
        {
            FileList.MoveSelection(move);
        }
        else
        {
            TileGrid.MoveSelection(move);
        }
    }

    /// <summary>Grid or list as the state says; the keyboard follows to the one shown.</summary>
    private void UpdateViewMode()
    {
        var list = Model?.ShowsList == true;
        var roots = Model?.HasRoots == true;
        var changed = ListScroller.IsVisible != (roots && list) || GridScroller.IsVisible != (roots && !list);
        GridScroller.IsVisible = roots && !list;
        ListScroller.IsVisible = roots && list;
        if (changed && (TileGrid.IsFocused || FileList.IsFocused))
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => FilesView.Focus(), Avalonia.Threading.DispatcherPriority.Input);
        }
    }

    /// <summary>The split view's active pane: an accent edge around it.</summary>
    public bool IsActive
    {
        get => ActiveEdge.IsVisible;
        set => ActiveEdge.IsVisible = value;
    }

    /// <summary>The top bar's size slider (off in the split view: Ctrl+wheel sizes each pane).</summary>
    public bool ShowsSizeSlider
    {
        get => Header.ShowsSizeSlider;
        set => Header.ShowsSizeSlider = value;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Header.Model = Model;
        TileGrid.Model = Model;
        FileList.Model = Model;
        UpdateViewMode();
        Header.ReturnFocusRequested -= OnHeaderReturnsFocus;
        Header.ReturnFocusRequested += OnHeaderReturnsFocus;
    }

    private void OnHeaderReturnsFocus(object? sender, EventArgs e) => FilesView.Focus();

    /// <summary>Selects <paramref name="assetId"/> if the grid shows it, scrolls to it and gives the grid the keyboard
    /// (back from the viewer, macOS reveal).</summary>
    public void Reveal(long assetId)
    {
        if (Model is { } model && model.Order.IndexOf(assetId) is { } index)
        {
            model.Selection.Select(assetId);
            if (model.ShowsList)
            {
                FileList.ScrollToAsset(assetId);
            }
            else
            {
                TileGrid.ScrollToVisible(index);
            }
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() => FilesView.Focus(), Avalonia.Threading.DispatcherPriority.Input);
    }

    /// <summary>Media files dropped from Explorer (not the app's own tiles dragged out): they open (W-89).</summary>
    public static List<string> DroppedMediaFiles(DragEventArgs e) =>
        IsDraggingOut ? [] : e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>()
            .Select(file => file.TryGetLocalPath())
            .OfType<string>()
            .Where(path => Core.Scanning.MediaClassifier.Classify(path) is not null)
            .ToList() ?? [];

    /// <summary>Media files were dropped on the pane: the window opens them.</summary>
    public event EventHandler<IReadOnlyList<string>>? FilesDropped;

    private static List<string> DroppedFolders(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.OfType<IStorageFolder>()
            .Select(folder => folder.TryGetLocalPath())
            .OfType<string>()
            .ToList() ?? [];

    /// <summary>A sidebar folder dragged here: its id.</summary>
    private static long? DroppedSidebarFolder(DragEventArgs e) =>
        long.TryParse(e.DataTransfer.TryGetValue(FolderFormat), out var id) ? id : null;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var sidebar = DroppedSidebarFolder(e) is not null;
        var files = DroppedMediaFiles(e).Count > 0;
        var accepts = Model is not null && (sidebar || files || DroppedFolders(e).Count > 0);
        e.DragEffects = !accepts ? DragDropEffects.None : sidebar ? DragDropEffects.Move : files ? DragDropEffects.Copy : DragDropEffects.Link;
        DropHighlight.IsVisible = accepts;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropHighlight.IsVisible = false;
        e.Handled = true;
        if (Model is not { } model)
        {
            return;
        }
        if (DroppedSidebarFolder(e) is { } folderId)
        {
            Activated?.Invoke(this, EventArgs.Empty);
            model.SelectFolder(folderId);
            return;
        }
        if (DroppedMediaFiles(e) is { Count: > 0 } files)
        {
            FilesDropped?.Invoke(this, files);
            return;
        }
        var folders = DroppedFolders(e);
        if (folders.Count > 0)
        {
            await model.AddFoldersAsync(folders);
        }
    }

    private void UpdateStatusLine()
    {
        StatusLine.IsVisible = Model is { HasRoots: true } && Settings.SettingsStore.Current.Settings.ShowStatusBar;
        // The bars lie over the files: the first and last rows can scroll clear of them, and the scroll bar starts
        // under the top bar.
        TileGrid.Insets = (Model is { HasRoots: true } ? Design.JGMetrics.BarHeight : 0,
            StatusLine.IsVisible ? Design.JGMetrics.StatusBubbleClearance : 0);
        FileList.Insets = TileGrid.Insets;
        if (_scrollBar is { } bar)
        {
            bar.Margin = new Avalonia.Thickness(0, TileGrid.Insets.Top, 0, 0);
        }
        if (_listScrollBar is { } listBar)
        {
            // Under the list's header too.
            listBar.Margin = new Avalonia.Thickness(0, TileGrid.Insets.Top + Design.JGMetrics.ControlHeightCompact, 0, 0);
        }
        UpdateViewMode();
    }

    private Avalonia.Controls.Primitives.ScrollBar? _scrollBar;
    private Avalonia.Controls.Primitives.ScrollBar? _listScrollBar;
}
