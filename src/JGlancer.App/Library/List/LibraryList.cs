using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using JGlancer.Core.Model;
using JGlancer.Design;
using JGlancer.Media.Thumbnails;

namespace JGlancer.App.Library.List;

/// <summary>
/// The library's list view (macOS D-125, MAC-041), as Explorer's Details and Finder's list: columns Name (a small
/// thumbnail or a glyph, the name, the flag star), Date Modified, Size and Kind; a click on a header sorts, its edge
/// widens or narrows the column, a drag moves it. With Include Subfolders the subfolders holding files of the query are
/// rows that unfold (<see cref="ListOutline"/>). One control draws the visible rows (never a control per row), fetches
/// their library rows and small thumbnails, and scrolls logically under the frosted top bar and the status bubble.
/// </summary>
internal sealed partial class LibraryList : Control, ILogicalScrollable
{
    private const double WheelStep = 64;
    private const int RowCapacity = 5000;

    private readonly Dictionary<long, Asset> _rows = [];
    private readonly Queue<long> _rowOrder = new();
    private readonly HashSet<long> _rowsInFlight = [];
    private readonly Dictionary<long, CancellationTokenSource> _thumbnailTasks = [];
    private readonly HashSet<long> _cloudMisses = [];
    private readonly DispatcherTimer _rowRefresh;
    private LibraryModel? _model;
    private ListOutline _outline = new();
    private IReadOnlyList<VisibleListRow> _visible = [];
    private OrderedIds _assetOrder = OrderedIds.Empty;
    private Vector _offset;
    private (double Top, double Bottom) _insets;
    private bool _rowsStale;

    public LibraryList()
    {
        Focusable = true;
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
        _rowRefresh = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => RefreshRows());
    }

    /// <summary>A right click on a file row (its asset id; the selection already holds it): the window shows the actions.</summary>
    public event EventHandler<long>? ContextMenuRequested;

    /// <summary>A right click on a folder row (its folder id): Open, Show in Explorer.</summary>
    public event EventHandler<long>? FolderMenuRequested;

    /// <summary>A row was dragged: the selection's files leave as a file drag (the press starts it).</summary>
    public event EventHandler<PointerPressedEventArgs>? DragRequested;

    public LibraryModel? Model
    {
        get => _model;
        set
        {
            if (_model is not null)
            {
                _model.RefsChanged -= OnRefsChanged;
                _model.StateChanged -= OnStateChanged;
                _model.TreesChanged -= OnRefsChanged;
                _model.Selection.Changed -= OnSelectionChanged;
                _model.Store.Changed -= OnStoreChanged;
                _model.FlagsChanged -= OnFlagsChanged;
            }
            _model = value;
            if (_model is not null)
            {
                _model.RefsChanged += OnRefsChanged;
                _model.StateChanged += OnStateChanged;
                _model.TreesChanged += OnRefsChanged;
                _model.Selection.Changed += OnSelectionChanged;
                _model.Store.Changed += OnStoreChanged;
                _model.FlagsChanged += OnFlagsChanged;
            }
            Rebuild();
        }
    }

    /// <summary>What lies over the list at its top (the frosted bar) and bottom (the status bubble).</summary>
    public (double Top, double Bottom) Insets
    {
        get => _insets;
        set
        {
            if (_insets != value)
            {
                _insets = value;
                Relayout();
            }
        }
    }

    /// <summary>The folder row chosen (a right click or the keyboard), when no file is.</summary>
    public long? FolderFocus { get; private set; }

    private double RowHeight => _model?.State.ListRowHeight ?? ListLayout.DefaultRowHeight;

    private static double HeaderHeight => JGMetrics.ControlHeightCompact;

    /// <summary>Where the first row starts, in content coordinates.</summary>
    private double RowsTop => _insets.Top + HeaderHeight;

    // MARK: ILogicalScrollable

    public bool CanHorizontallyScroll { get; set; }

    public bool CanVerticallyScroll { get; set; } = true;

    public bool IsLogicalScrollEnabled => true;

    public Size ScrollSize => new(0, WheelStep);

    public Size PageScrollSize => new(0, Math.Max(WheelStep, Bounds.Height - RowsTop - _insets.Bottom - RowHeight));

    public Size Extent => new(Bounds.Width, Math.Max(RowsTop + _visible.Count * RowHeight + _insets.Bottom, Bounds.Height));

    public Size Viewport => Bounds.Size;

    public Vector Offset
    {
        get => _offset;
        set
        {
            var clamped = new Vector(0, Math.Clamp(value.Y, 0, Math.Max(0, Extent.Height - Bounds.Height)));
            if (clamped == _offset)
            {
                return;
            }
            _offset = clamped;
            InvalidateVisual();
            UpdateWindow();
        }
    }

    public event EventHandler? ScrollInvalidated;

    public bool BringIntoView(Control target, Rect targetRect) => false;

    public Control? GetControlInDirection(NavigationDirection direction, Control? from) => null;

    public void RaiseScrollInvalidated(EventArgs e) => ScrollInvalidated?.Invoke(this, e);

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Relayout();
    }

    private void Relayout()
    {
        _offset = new Vector(0, Math.Clamp(_offset.Y, 0, Math.Max(0, Extent.Height - Bounds.Height)));
        RaiseScrollInvalidated(EventArgs.Empty);
        InvalidateVisual();
        UpdateWindow();
    }

    // MARK: Rows

    /// <summary>The rows again: the query, its folders, the unfolded ones.</summary>
    private void Rebuild()
    {
        if (_model is not { } model)
        {
            _outline = new ListOutline();
            _visible = [];
            _assetOrder = OrderedIds.Empty;
            Relayout();
            return;
        }
        _outline = new ListOutline(model.Refs, model.ListTree, foldersDescending: model.State.Sort == AssetSort.Name && !model.State.Ascending);
        Reshow();
    }

    /// <summary>The visible rows after a fold or unfold.</summary>
    private void Reshow()
    {
        _visible = _outline.Visible(_model?.ListExpanded ?? []);
        _assetOrder = new OrderedIds([.. _visible.Where(r => !r.Row.IsFolder).Select(r => r.Row.Id)]);
        if (FolderFocus is { } folder && !_visible.Any(r => r.Row.IsFolder && r.Row.Id == folder))
        {
            FolderFocus = null;
        }
        Relayout();
    }

    private void OnRefsChanged(object? sender, EventArgs e)
    {
        CancelThumbnails();
        Rebuild();
    }

    private double _shownRowHeight = ListLayout.DefaultRowHeight;

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_model is { } model && model.State.ListRowHeight != _shownRowHeight)
        {
            _shownRowHeight = model.State.ListRowHeight;
            _texts.Clear();
        }
        Relayout();
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_model is { Selection.IsEmpty: false })
        {
            FolderFocus = null;
        }
        InvalidateVisual();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        _rowsStale = true;
        if (!_rowRefresh.IsEnabled)
        {
            _rowRefresh.Start();
        }
    });

    private void OnFlagsChanged(object? sender, EventArgs e)
    {
        _rowsStale = true;
        RefreshRows();
    }

    private void RefreshRows()
    {
        _rowRefresh.Stop();
        if (!_rowsStale)
        {
            return;
        }
        _rowsStale = false;
        var ids = WindowAssetIds().ToList();
        if (ids.Count > 0)
        {
            FetchRows(ids, replace: true);
        }
    }

    /// <summary>Visible row indices: start inclusive, end exclusive.</summary>
    private (int Start, int End) VisibleRange()
    {
        var height = RowHeight;
        var start = Math.Max(0, (int)Math.Floor((_offset.Y - RowsTop) / height));
        var end = Math.Min(_visible.Count, (int)Math.Ceiling((_offset.Y + Bounds.Height - RowsTop) / height) + 1);
        return (Math.Min(start, _visible.Count), Math.Max(start, end));
    }

    /// <summary>The asset ids on screen and half a screen around it.</summary>
    private IEnumerable<long> WindowAssetIds()
    {
        var (start, end) = VisibleRange();
        var margin = (end - start) / 2 + 1;
        for (var i = Math.Max(0, start - margin); i < Math.Min(_visible.Count, end + margin); i++)
        {
            if (!_visible[i].Row.IsFolder)
            {
                yield return _visible[i].Row.Id;
            }
        }
    }

    /// <summary>Fetches the rows of the window that are not here yet, and the small thumbnails of the visible ones.</summary>
    private void UpdateWindow()
    {
        if (_model is null)
        {
            return;
        }
        var missing = WindowAssetIds().Where(id => !_rows.ContainsKey(id) && !_rowsInFlight.Contains(id)).ToList();
        if (missing.Count > 0)
        {
            FetchRows(missing, replace: false);
        }
        var (start, end) = VisibleRange();
        for (var i = start; i < end; i++)
        {
            if (!_visible[i].Row.IsFolder && _rows.TryGetValue(_visible[i].Row.Id, out var asset))
            {
                RequestThumbnail(asset);
            }
        }
    }

    private void FetchRows(List<long> ids, bool replace)
    {
        if (_model is null)
        {
            return;
        }
        _rowsInFlight.UnionWith(ids);
        var store = _model.Store;
        _ = Load();
        async Task Load()
        {
            var assets = await store.AssetsAsync(ids);
            _rowsInFlight.ExceptWith(ids);
            foreach (var asset in assets)
            {
                if (_rows.ContainsKey(asset.Id))
                {
                    if (!replace)
                    {
                        continue;
                    }
                }
                else
                {
                    _rowOrder.Enqueue(asset.Id);
                }
                if (!asset.IsCloudOnly)
                {
                    _cloudMisses.Remove(asset.Id);
                }
                _rows[asset.Id] = asset;
            }
            while (_rowOrder.Count > RowCapacity)
            {
                _rows.Remove(_rowOrder.Dequeue());
            }
            InvalidateVisual();
            UpdateWindow();
        }
    }

    private ThumbnailCache Thumbnails => _model!.Services.Bitmaps;

    private void RequestThumbnail(Asset asset)
    {
        var request = ThumbnailRequest.For(asset, ThumbnailBucket.Small);
        if (_model is null || Thumbnails.Contains(request.Key) || _thumbnailTasks.ContainsKey(asset.Id)
            || (asset.IsCloudOnly && _cloudMisses.Contains(asset.Id)))
        {
            return;
        }
        var cancel = new CancellationTokenSource();
        _thumbnailTasks[asset.Id] = cancel;
        var service = _model.Thumbnails;
        _ = Load();
        async Task Load()
        {
            var image = await service.GetAsync(request, ThumbnailPriority.Visible, cancel.Token);
            if (cancel.IsCancellationRequested)
            {
                return;
            }
            _thumbnailTasks.Remove(asset.Id);
            if (image is null && asset.IsCloudOnly)
            {
                _cloudMisses.Add(asset.Id);
                InvalidateVisual();
                return;
            }
            var bitmap = image is null ? null : await Task.Run(() => ThumbnailCache.ToBitmap(image));
            if (cancel.IsCancellationRequested)
            {
                bitmap?.Dispose();
                return;
            }
            Thumbnails.Insert(request.Key, bitmap);
            InvalidateVisual();
        }
    }

    private void CancelThumbnails()
    {
        foreach (var cancel in _thumbnailTasks.Values)
        {
            cancel.Cancel();
        }
        _thumbnailTasks.Clear();
    }

    // MARK: Showing a file

    /// <summary>The visible row of a file, unfolding its folders first.</summary>
    private int? IndexOfAsset(long assetId, bool unfold)
    {
        if (unfold && _model is { } model)
        {
            var path = _outline.FolderPath(assetId);
            if (path.Any(folder => !model.ListExpanded.Contains(folder)))
            {
                model.ListExpanded.UnionWith(path);
                Reshow();
            }
        }
        for (var i = 0; i < _visible.Count; i++)
        {
            if (!_visible[i].Row.IsFolder && _visible[i].Row.Id == assetId)
            {
                return i;
            }
        }
        return null;
    }

    /// <summary>Scrolls so the file's row shows (its folders unfolded).</summary>
    public void ScrollToAsset(long assetId)
    {
        if (IndexOfAsset(assetId, unfold: true) is { } index)
        {
            ScrollToRow(index);
        }
    }

    private void ScrollToRow(int index)
    {
        var top = RowsTop + index * RowHeight;
        if (top < _offset.Y + RowsTop)
        {
            Offset = new Vector(0, index * RowHeight);
        }
        else if (top + RowHeight > _offset.Y + Bounds.Height - _insets.Bottom)
        {
            Offset = new Vector(0, top + RowHeight + _insets.Bottom - Bounds.Height);
        }
        RaiseScrollInvalidated(EventArgs.Empty);
    }

    /// <summary>The name's place in a file's row, in this control's coordinates (renaming in place), or null.</summary>
    public Rect? NameRect(long assetId)
    {
        if (IndexOfAsset(assetId, unfold: true) is not { } index)
        {
            return null;
        }
        ScrollToRow(index);
        var (x, width) = NameTextSpan(_visible[index].Depth);
        return new Rect(x, RowsTop + index * RowHeight - _offset.Y, width, RowHeight);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Settings.SettingsStore.Current.Changed += OnSettingsChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Settings.SettingsStore.Current.Changed -= OnSettingsChanged;
        CancelThumbnails();
        _rowRefresh.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnSettingsChanged(object? sender, (Core.Settings.AppSettings Old, Core.Settings.AppSettings New) change)
    {
        _texts.Clear();
        InvalidateVisual();
    }
}
