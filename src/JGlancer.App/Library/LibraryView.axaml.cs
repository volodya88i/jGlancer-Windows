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
        AttachSidebarFold();
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

    /// <summary>The active pane's list (View ▸ as List).</summary>
    public List.LibraryList LibraryList => _activePane.List;

    /// <summary>The active pane's grid or list, whichever shows the files.</summary>
    public Avalonia.Controls.Control FilesView => _activePane.FilesView;

    /// <summary>Moves the selection in the active pane as an arrow key does (Quick Preview's arrows).</summary>
    public void MoveSelection(GridMove move) => _activePane.MoveSelection(move);

    /// <summary>The pane showing <paramref name="model"/> (the main one when none does).</summary>
    public BrowserPane PaneOf(LibraryModel? model) =>
        _secondPane is { } second && ReferenceEquals(second.Model, model) ? second : PrimaryPane;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Sidebar.Model = Model;
        if (Model is { } model)
        {
            model.StateChanged += (_, _) => UpdateSidebarFold();
            UpdateSidebarFold();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // A dialog started in the sidebar over the files ended: it may go now.
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.Activated += (_, _) => CloseIfLeft();
        }
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
    private SizeMap.SizeMapModel? _sizeMap;
    private (double Size, double Start)? _rightDrag;
    private (double Size, double Start)? _bottomDrag;

    /// <summary>
    /// The side modules (macOS D-127): the preview, shown while the library has folders and View ▸ Show Preview is on,
    /// and the size map (View ▸ Show Size Map), each right of the files or under them.
    /// </summary>
    public void AttachPreview(PreviewModel preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (Model is not { } model)
        {
            return;
        }
        _preview = preview;
        Pane.Attach(preview, model);
        _sizeMap = new SizeMap.SizeMapModel(model);
        SizeMap.Attach(_sizeMap);
        SizeMap.RevealRequested += (_, id) => PrimaryPane.Reveal(id);
        model.StateChanged += (_, _) => UpdateModules();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LibraryModel.HasRoots))
            {
                UpdateModules();
            }
        };
        AttachPlaceHandles(model);
        UpdateModules();
    }

    /// <summary>The right place's left edge drags its width; the place under the files drags by its top edge.</summary>
    private void AttachPlaceHandles(LibraryModel model)
    {
        RightHandle.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(RightHandle).Properties.IsLeftButtonPressed)
            {
                _rightDrag = (model.State.PreviewWidth, e.GetPosition(null).X);
                e.Pointer.Capture(RightHandle);
                e.Handled = true;
            }
        };
        RightHandle.PointerMoved += (_, e) =>
        {
            if (_rightDrag is { } drag)
            {
                model.SetPreviewWidth(drag.Size - (e.GetPosition(null).X - drag.Start));
            }
        };
        RightHandle.PointerReleased += (_, e) =>
        {
            _rightDrag = null;
            e.Pointer.Capture(null);
        };
        BottomHandle.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(BottomHandle).Properties.IsLeftButtonPressed)
            {
                _bottomDrag = (model.State.BottomHeight, e.GetPosition(null).Y);
                e.Pointer.Capture(BottomHandle);
                e.Handled = true;
            }
        };
        BottomHandle.PointerMoved += (_, e) =>
        {
            if (_bottomDrag is { } drag)
            {
                model.SetBottomHeight(drag.Size - (e.GetPosition(null).Y - drag.Start));
            }
        };
        BottomHandle.PointerReleased += (_, e) =>
        {
            _bottomDrag = null;
            e.Pointer.Capture(null);
        };
    }

    /// <summary>
    /// The module shown in a place, if it can show: the preview needs its model, the size map no split view (they exclude
    /// each other for now, D-127); neither shows before the library has a folder.
    /// </summary>
    private Core.Model.LibraryModule? ShownModule(Core.Model.ModuleSlot slot)
    {
        if (Model is not { HasRoots: true } model || model.State.ModuleIn(slot) is not { } module)
        {
            return null;
        }
        return module == Core.Model.LibraryModule.Preview
            ? _preview is null ? null : module
            : _sizeMap is null || model.State.Split != SplitMode.Off ? null : module;
    }

    private void UpdateModules()
    {
        if (Model is not { } model || _preview is null)
        {
            return;
        }
        var right = ShownModule(Core.Model.ModuleSlot.Right);
        var bottom = ShownModule(Core.Model.ModuleSlot.Bottom);
        Place(Pane, Core.Model.LibraryModule.Preview, right, bottom);
        Place(SizeMap, Core.Model.LibraryModule.SizeMap, right, bottom);
        RightPlace.IsVisible = RightHairline.IsVisible = RightHandle.IsVisible = right is not null;
        BottomPlace.IsVisible = BottomHairline.IsVisible = BottomHandle.IsVisible = bottom is not null;
        // A narrow window: the right place, then the sidebar give way (down to their minimums) before the panes go under
        // LibraryPaneMinWidth; the chosen widths stay saved (macOS MAC-020).
        var width = Bounds.Width > 0 ? Bounds.Width : double.PositiveInfinity;
        var sidebarRoom = WantsDocked ? JGMetrics.SidebarMinWidth : JGMetrics.SidebarStripWidth;
        var rightWidth = right is not null
            ? Math.Max(JGMetrics.PreviewPaneMinWidth, Math.Min(model.State.PreviewWidth, width - JGMetrics.LibraryPaneMinWidth - sidebarRoom))
            : 0;
        RightPlace.Width = right is not null ? rightWidth : model.State.PreviewWidth;
        _dockedWidth = Math.Max(JGMetrics.SidebarMinWidth, Math.Min(model.State.SidebarWidth, width - JGMetrics.LibraryPaneMinWidth - rightWidth));
        UpdateSidebarFold();
        // The place under the files: its chosen height, lower when the window leaves the files too little room.
        var height = Bounds.Height > 0 ? Bounds.Height : double.PositiveInfinity;
        BottomPlace.Height = Math.Max(JGMetrics.BottomModuleMinHeight, Math.Min(model.State.BottomHeight, height - JGMetrics.BottomModuleMinHeight));
        var previewShown = right == Core.Model.LibraryModule.Preview || bottom == Core.Model.LibraryModule.Preview;
        _preview.SetPaneShown(previewShown && IsVisible);
        var mapShown = right == Core.Model.LibraryModule.SizeMap || bottom == Core.Model.LibraryModule.SizeMap;
        SizeMap.IsActive = mapShown && IsVisible;
    }

    /// <summary>Puts a module in the place it is shown in (it moves between the places), or hides it.</summary>
    private void Place(Control module, Core.Model.LibraryModule which, Core.Model.LibraryModule? right, Core.Model.LibraryModule? bottom)
    {
        var slot = right == which ? Core.Model.ModuleSlot.Right : bottom == which ? Core.Model.ModuleSlot.Bottom : (Core.Model.ModuleSlot?)null;
        module.IsVisible = slot is not null;
        if (slot is not { } place)
        {
            return;
        }
        var host = place == Core.Model.ModuleSlot.Right ? RightPlace : BottomPlace;
        if (!ReferenceEquals(module.Parent, host))
        {
            if (module is PreviewPane)
            {
                _preview?.SurfaceMoving();
            }
            (module.Parent as Panel)?.Children.Remove(module);
            host.Children.Add(module);
        }
        if (module is SizeMap.SizeMapModule map)
        {
            map.Slot = place;
        }
        else if (module is PreviewPane pane)
        {
            pane.Slot = place;
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateModules();
        UpdateSidebarFold();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Hidden behind a viewer: the modules show nothing.
        if (change.Property == IsVisibleProperty)
        {
            UpdateModules();
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
