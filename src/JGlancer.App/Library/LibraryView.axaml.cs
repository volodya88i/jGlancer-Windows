using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using JGlancer.App.Preview;
using JGlancer.App.Support;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.Library;

/// <summary>
/// The library screen: the sidebar (its width dragged by its edge), one pane — or two, side by side or stacked, with a
/// divider between them (split view) — and the preview pane, which shows the active pane's selection.
/// </summary>
internal sealed partial class LibraryView : UserControl
{
    private BrowserPane? _secondPane;
    private GridSplitter? _splitter;
    private BrowserPane _activePane;

    public LibraryView()
    {
        InitializeComponent();
        _activePane = PrimaryPane;
        AttachPane(PrimaryPane);
        Sidebar.ActiveModel = () => ActiveModel;
        SidebarHandle.PointerPressed += (_, e) =>
        {
            if (Model is { } model && e.GetCurrentPoint(SidebarHandle).Properties.IsLeftButtonPressed)
            {
                _sidebarDrag = (model.State.SidebarWidth, e.GetPosition(null).X);
                e.Pointer.Capture(SidebarHandle);
                e.Handled = true;
            }
        };
        SidebarHandle.PointerMoved += (_, e) =>
        {
            if (_sidebarDrag is { } drag && Model is { } model)
            {
                model.SetSidebarWidth(drag.Width + e.GetPosition(null).X - drag.X);
            }
        };
        SidebarHandle.PointerReleased += (_, e) =>
        {
            _sidebarDrag = null;
            e.Pointer.Capture(null);
        };
    }

    private (double Width, double X)? _sidebarDrag;

    /// <summary>Copies an album's files into a folder chosen then (set by the window: file operations).</summary>
    public Func<IReadOnlyList<string>, Task>? ExportFiles
    {
        get => Sidebar.ExportFiles;
        set => Sidebar.ExportFiles = value;
    }

    /// <summary>Sends files dropped on the Trash row to the Recycle Bin (set by the window: file operations).</summary>
    public Func<IReadOnlyList<string>, Task>? TrashFiles
    {
        get => Sidebar.TrashFiles;
        set => Sidebar.TrashFiles = value;
    }

    /// <summary>Makes a subfolder on disk (set by the window: file operations).</summary>
    public Func<string, string, Task>? CreateFolder
    {
        get => Sidebar.CreateFolder;
        set => Sidebar.CreateFolder = value;
    }

    /// <summary>Sends a folder to the Recycle Bin (set by the window: file operations).</summary>
    public Func<string, Task>? TrashFolder
    {
        get => Sidebar.TrashFolder;
        set => Sidebar.TrashFolder = value;
    }

    /// <summary>Moves or copies dropped files into a library folder (set by the window: file operations).</summary>
    public Func<IReadOnlyList<string>, string, bool, Task>? DropFiles
    {
        get => Sidebar.DropFiles;
        set => Sidebar.DropFiles = value;
    }

    private Func<string, string, Task<string?>>? _renameFile;

    /// <summary>Renames a file in place (set by the window: file operations); every pane uses it.</summary>
    public Func<string, string, Task<string?>>? RenameFile
    {
        get => _renameFile;
        set
        {
            _renameFile = value;
            PrimaryPane.RenameFile = value;
            _secondPane?.RenameFile = value;
        }
    }

    private Action<string>? _showMessage;

    /// <summary>Shows a file operation's problem (set by the window); every pane uses it.</summary>
    public Action<string>? ShowMessage
    {
        get => _showMessage;
        set
        {
            _showMessage = value;
            PrimaryPane.ShowMessage = value;
            _secondPane?.ShowMessage = value;
        }
    }

    /// <summary>The main library model (the sidebar's, the first pane's).</summary>
    public LibraryModel? Model => DataContext as LibraryModel;

    /// <summary>The pane actions, the preview and the sidebar's clicks apply to.</summary>
    public BrowserPane ActivePane => _activePane;

    public LibraryModel? ActiveModel => _activePane.Model ?? Model;

    /// <summary>Another pane became the active one.</summary>
    public event EventHandler? ActivePaneChanged;

    /// <summary>Media files from Explorer were dropped on a pane (W-89).</summary>
    public event EventHandler<IReadOnlyList<string>>? FilesDropped;

    /// <summary>The active pane's grid (the window's keys, menus and developer hooks).</summary>
    public Grid.LibraryGrid LibraryGrid => _activePane.Grid;

    /// <summary>The pane showing <paramref name="model"/> (the main one when none does).</summary>
    public BrowserPane PaneOf(LibraryModel? model) =>
        _secondPane is { } second && ReferenceEquals(second.Model, model) ? second : PrimaryPane;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Sidebar.Model = Model;
    }

    private void AttachPane(BrowserPane pane)
    {
        pane.AddFolderRequested += async (_, _) => await AddFolderAsync();
        pane.FilesDropped += (_, files) => FilesDropped?.Invoke(this, files);
        pane.Activated += (_, _) => Activate(pane);
        pane.RenameFile = _renameFile;
        pane.ShowMessage = _showMessage;
    }

    private void Activate(BrowserPane pane)
    {
        if (ReferenceEquals(pane, _activePane))
        {
            return;
        }
        _activePane = pane;
        UpdateActiveEdges();
        Sidebar.Refresh();
        ActivePaneChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateActiveEdges()
    {
        PrimaryPane.IsActive = _secondPane is not null && ReferenceEquals(_activePane, PrimaryPane);
        _secondPane?.IsActive = ReferenceEquals(_activePane, _secondPane);
    }

    /// <summary>
    /// One pane (<paramref name="second"/> null), or two side by side or stacked with a divider at
    /// <paramref name="ratio"/>. The size slider steps aside while split: Ctrl+wheel sizes each pane.
    /// </summary>
    public void SetSplit(SplitMode mode, LibraryModel? second, double ratio)
    {
        PaneArea.Children.Clear();
        PaneArea.ColumnDefinitions.Clear();
        PaneArea.RowDefinitions.Clear();
        _splitter = null;
        var wasActiveSecond = _secondPane is not null && ReferenceEquals(_activePane, _secondPane);
        if (mode == SplitMode.Off || second is null)
        {
            _secondPane = null;
            PaneArea.Children.Add(PrimaryPane);
            PrimaryPane.ShowsSizeSlider = true;
            _activePane = PrimaryPane;
            UpdateActiveEdges();
            Sidebar.Refresh();
            if (wasActiveSecond)
            {
                ActivePaneChanged?.Invoke(this, EventArgs.Empty);
            }
            return;
        }
        if (_secondPane is null || !ReferenceEquals(_secondPane.Model, second))
        {
            _secondPane = new BrowserPane { DataContext = second };
            AttachPane(_secondPane);
            second.StateChanged += (_, _) =>
            {
                if (ReferenceEquals(_activePane.Model, second))
                {
                    Sidebar.Refresh();
                }
            };
        }
        var sideBySide = mode == SplitMode.SideBySide;
        var first = new GridLength(Math.Clamp(ratio, MinimumShare, 1 - MinimumShare), GridUnitType.Star);
        var rest = new GridLength(1 - first.Value, GridUnitType.Star);
        var handle = new GridLength(JGMetrics.ResizeHandleWidth);
        if (sideBySide)
        {
            PaneArea.ColumnDefinitions.Add(new ColumnDefinition(first) { MinWidth = MinimumPaneSize });
            PaneArea.ColumnDefinitions.Add(new ColumnDefinition(handle));
            PaneArea.ColumnDefinitions.Add(new ColumnDefinition(rest) { MinWidth = MinimumPaneSize });
        }
        else
        {
            PaneArea.RowDefinitions.Add(new RowDefinition(first) { MinHeight = MinimumPaneSize });
            PaneArea.RowDefinitions.Add(new RowDefinition(handle));
            PaneArea.RowDefinitions.Add(new RowDefinition(rest) { MinHeight = MinimumPaneSize });
        }
        _splitter = new GridSplitter
        {
            ResizeDirection = sideBySide ? GridResizeDirection.Columns : GridResizeDirection.Rows,
            Background = Avalonia.Media.Brushes.Transparent,
            Cursor = new Cursor(sideBySide ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth),
        };
        // The divider: a hairline in the middle of a wider handle, easy to grab.
        var line = new Border
        {
            Width = sideBySide ? JGMetrics.OutlineWidth : double.NaN,
            Height = sideBySide ? double.NaN : JGMetrics.OutlineWidth,
            HorizontalAlignment = sideBySide ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
            VerticalAlignment = sideBySide ? VerticalAlignment.Stretch : VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        line.Bind(Border.BackgroundProperty, line.GetResourceObservable(JGColor.LineStrong.ResourceKey));
        _splitter.DragCompleted += (_, _) => SplitRatioChanged?.Invoke(this, CurrentRatio());
        Place(PrimaryPane, 0, sideBySide);
        Place(_splitter, 1, sideBySide);
        Place(line, 1, sideBySide);
        Place(_secondPane, 2, sideBySide);
        PaneArea.Children.AddRange([PrimaryPane, _splitter, line, _secondPane]);
        PrimaryPane.ShowsSizeSlider = false;
        _secondPane.ShowsSizeSlider = false;
        UpdateActiveEdges();
    }

    /// <summary>The divider was dragged: the first pane's new share (saved with the view state).</summary>
    public event EventHandler<double>? SplitRatioChanged;

    /// <summary>A pane never gets narrower or lower than this (effective pixels) …</summary>
    private const double MinimumPaneSize = 240;

    /// <summary>… nor a smaller share of the split.</summary>
    private const double MinimumShare = 0.15;

    private double CurrentRatio()
    {
        var sizes = PaneArea.ColumnDefinitions.Count == 3
            ? (PaneArea.ColumnDefinitions[0].ActualWidth, PaneArea.ColumnDefinitions[2].ActualWidth)
            : (PaneArea.RowDefinitions[0].ActualHeight, PaneArea.RowDefinitions[2].ActualHeight);
        var total = sizes.Item1 + sizes.Item2;
        return total <= 0 ? 0.5 : sizes.Item1 / total;
    }

    private static void Place(Control control, int index, bool sideBySide)
    {
        if (sideBySide)
        {
            Avalonia.Controls.Grid.SetColumn(control, index);
            Avalonia.Controls.Grid.SetRow(control, 0);
        }
        else
        {
            Avalonia.Controls.Grid.SetRow(control, index);
            Avalonia.Controls.Grid.SetColumn(control, 0);
        }
    }

    private PreviewModel? _preview;

    /// <summary>The preview pane on the right, shown while the library has folders and View ▸ Show Preview is on.</summary>
    public void AttachPreview(PreviewModel preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (Model is not { } model)
        {
            return;
        }
        _preview = preview;
        Pane.Attach(preview, model);
        model.StateChanged += (_, _) => UpdatePreviewPane();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LibraryModel.HasRoots))
            {
                UpdatePreviewPane();
            }
        };
        UpdatePreviewPane();
    }

    private void UpdatePreviewPane()
    {
        if (Model is not { } model || _preview is null)
        {
            return;
        }
        var shown = model.HasRoots && model.State.PreviewVisible;
        Pane.IsVisible = PreviewHairline.IsVisible = shown;
        Pane.Width = model.State.PreviewWidth;
        Columns.ColumnDefinitions[0].Width = new GridLength(model.State.SidebarWidth);
        _preview.SetPaneShown(shown && IsVisible);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Hidden behind a viewer: the pane shows nothing.
        if (change.Property == IsVisibleProperty)
        {
            UpdatePreviewPane();
        }
    }

    /// <summary>Selects <paramref name="assetId"/> in the active pane if it shows it, and gives its grid the keyboard.</summary>
    public void Reveal(long assetId) => _activePane.Reveal(assetId);

    /// <summary>Renames a file in place in the active pane.</summary>
    public void BeginRename(long assetId) => _activePane.BeginRename(assetId);

    public async Task AddFolderAsync()
    {
        if (Model is { } model && TopLevel.GetTopLevel(this) is { } top)
        {
            await model.AddFoldersAsync(await FolderPicker.ChooseAsync(top));
        }
    }
}
