using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using JGlancer.Core.Model;
using JGlancer.Design;
using JGlancer.Media.Thumbnails;

namespace JGlancer.App.Library.Grid;

/// <summary>
/// The library grid (macOS GridController + GridLayout + tiles): one control that draws only the visible tiles
/// (never a control per item), scrolls logically through <see cref="GridGeometry"/>, fetches full rows and thumbnails
/// for the visible window plus 1.5 screens around it and cancels the rest (macOS ARCHITECTURE §6–7).
/// </summary>
internal sealed partial class LibraryGrid : Control, ILogicalScrollable
{
    private const int RowCapacity = 5000;
    // Half a screen above and below: a jump starts few requests, so the visible tiles come first (W-37).
    private const double PrefetchScreens = 0.5;
    private const double WheelStep = 64;

    private readonly Dictionary<long, Asset> _rows = [];
    private readonly Queue<long> _rowOrder = new();
    private readonly HashSet<long> _rowsInFlight = [];
    private readonly Dictionary<long, (CancellationTokenSource Cancel, ThumbnailPriority Priority, ThumbnailKey Key)> _thumbnailTasks = [];
    private readonly DispatcherTimer _rowRefresh;
    private LibraryModel? _model;
    private GridGeometry _geometry = new(0, 0, JGMetrics.TileSideDefault, JGMetrics.GridGutter, JGMetrics.GridInset, 0);
    private Vector _offset;
    private ThumbnailBucket _bucket = ThumbnailBucket.Medium;
    private bool _rowsStale;

    public LibraryGrid()
    {
        Focusable = true;
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
        // Metadata arriving in the background (durations, sizes) refreshes the visible rows at most twice a second.
        _rowRefresh = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => RefreshRows());
    }

    /// <summary>Display-ready thumbnails, shared with the viewer (placeholder, filmstrip).</summary>
    private ThumbnailCache Thumbnails => _model!.Services.Bitmaps;

    public LibraryModel? Model
    {
        get => _model;
        set
        {
            if (_model is not null)
            {
                _model.RefsChanged -= OnRefsChanged;
                _model.StateChanged -= OnStateChanged;
                _model.Selection.Changed -= OnSelectionChanged;
                _model.Store.Changed -= OnStoreChanged;
                _model.FlagsChanged -= OnFlagsChanged;
            }
            _model = value;
            if (_model is not null)
            {
                _model.RefsChanged += OnRefsChanged;
                _model.StateChanged += OnStateChanged;
                _model.Selection.Changed += OnSelectionChanged;
                _model.Store.Changed += OnStoreChanged;
                _model.FlagsChanged += OnFlagsChanged;
            }
            Relayout();
        }
    }

    /// <summary>The current layout (for benchmarks and tests).</summary>
    public GridGeometry Geometry => _geometry;

    /// <summary>Thumbnails requested and not arrived yet (snapshots wait for zero).</summary>
    public int PendingThumbnails => _thumbnailTasks.Count;

    /// <summary>True when every visible tile shows its thumbnail or is known to have none (benchmarks).</summary>
    public bool IsVisibleComplete
    {
        get
        {
            if (_model is null)
            {
                return true;
            }
            var (start, end) = VisibleRange();
            for (var index = start; index < end && index < Refs.Count; index++)
            {
                if (!_rows.TryGetValue(Refs[index].Id, out var asset) || !Thumbnails.Contains(ThumbnailRequest.For(asset, _bucket).Key))
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>Rows requested from the store and not arrived yet.</summary>
    public int PendingRows => _rowsInFlight.Count;

    private IReadOnlyList<AssetRef> Refs => _model?.Refs ?? [];

    private bool CaptionsVisible => _model is { } model && model.State.ShowNames && model.State.TileSide >= JGMetrics.TileCaptionMinimumSide;

    // MARK: ILogicalScrollable

    public bool CanHorizontallyScroll { get; set; }

    public bool CanVerticallyScroll { get; set; } = true;

    public bool IsLogicalScrollEnabled => true;

    public Size ScrollSize => new(0, WheelStep);

    public Size PageScrollSize => new(0, Math.Max(WheelStep, Bounds.Height - _geometry.CellHeight));

    public Size Extent => new(Bounds.Width, Math.Max(_geometry.ContentHeight, Bounds.Height));

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
            var previous = _offset.Y;
            _offset = clamped;
            InvalidateVisual();
            TrackScroll(previous);
            UpdateWindow();
        }
    }

    public event EventHandler? ScrollInvalidated;

    public bool BringIntoView(Control target, Rect targetRect) => false;

    public Control? GetControlInDirection(NavigationDirection direction, Control? from) => null;

    public void RaiseScrollInvalidated(EventArgs e) => ScrollInvalidated?.Invoke(this, e);

    // MARK: Layout

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Relayout();
        if (_pendingScroll is { } index && Bounds.Width > 0 && Bounds.Height > 0)
        {
            _pendingScroll = null;
            ScrollToVisible(index);
        }
    }

    private int? _pendingScroll;

    /// <summary>Recomputes the geometry for the width, tile side, names and item count.</summary>
    private void Relayout()
    {
        var state = _model?.State;
        _geometry = new GridGeometry(
            Refs.Count, Bounds.Width, state?.TileSide ?? JGMetrics.TileSideDefault, JGMetrics.GridGutter, JGMetrics.GridInset,
            CaptionsVisible ? JGMetrics.TileCaptionHeight : 0);
        var bucket = ThumbnailBuckets.Covering(_geometry.TileSide * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));
        if (bucket != _bucket)
        {
            _bucket = bucket;
            CancelAllThumbnails();
        }
        _offset = new Vector(0, Math.Clamp(_offset.Y, 0, Math.Max(0, Extent.Height - Bounds.Height)));
        RaiseScrollInvalidated(EventArgs.Empty);
        InvalidateVisual();
        UpdateWindow();
    }

    /// <summary>Scrolls so the tile at <paramref name="index"/> is fully visible (once laid out, if not yet).</summary>
    public void ScrollToVisible(int index)
    {
        if (index < 0 || index >= _geometry.ItemCount)
        {
            return;
        }
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            _pendingScroll = index;
            return;
        }
        var frame = _geometry.Frame(index);
        if (frame.Y - _geometry.Gutter < _offset.Y)
        {
            Offset = new Vector(0, frame.Y - _geometry.Inset);
        }
        else if (frame.Bottom + _geometry.Gutter > _offset.Y + Bounds.Height)
        {
            Offset = new Vector(0, frame.Bottom + _geometry.Inset - Bounds.Height);
        }
        RaiseScrollInvalidated(EventArgs.Empty);
    }

    // MARK: Model events

    private void OnRefsChanged(object? sender, EventArgs e)
    {
        var drawn = DrawnNow();
        var count = _geometry.ItemCount;
        CancelAllThumbnails();
        Relayout();
        if (_model?.TakeSequenceToggle() is { } toggle)
        {
            // A sequence folded or unfolded: its frames fly out of the stack or back into it (W-81).
            AnimateSequence(toggle, drawn);
        }
        else if (_model is { } model && count == Refs.Count && drawn.Keys.All(model.Order.Contains))
        {
            // The same files in another order (sort): they glide to their places; another folder or filter just shows.
            StartReflow(drawn);
        }
        if (_model?.Selection.Focus is { } focus && _model.Order.IndexOf(focus) is { } index && !_restoredFocus)
        {
            _restoredFocus = true;
            ScrollToVisible(index);
        }
    }

    private bool _restoredFocus;

    private void OnStateChanged(object? sender, EventArgs e)
    {
        var before = _geometry;
        var drawn = DrawnNow();
        // A new tile size keeps the focused tile (or the first one on screen) where it is on screen.
        int? anchor = null;
        double anchorY = 0;
        if (_model is { } model && model.State.TileSide != before.TileSide && before.ItemCount > 0)
        {
            var (start, end) = VisibleRange();
            anchor = model.Selection.Focus is { } focus && model.Order.IndexOf(focus) is { } at && at >= start && at < end ? at : start;
            anchorY = before.Frame(anchor.Value).Y - _offset.Y;
        }
        Relayout();
        if (anchor is { } index && index < _geometry.ItemCount)
        {
            _offset = new Vector(0, Math.Clamp(_geometry.Frame(index).Y - anchorY, 0, Math.Max(0, Extent.Height - Bounds.Height)));
            RaiseScrollInvalidated(EventArgs.Empty);
            UpdateWindow();
        }
        if (_geometry.TileSide != before.TileSide || _geometry.CellHeight != before.CellHeight || _geometry.Columns != before.Columns)
        {
            StartReflow(drawn);
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        FadeSelectionChanges();
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

    /// <summary>A flag changed by the user: the stars show at once, not after the burst timer.</summary>
    private void OnFlagsChanged(object? sender, EventArgs e)
    {
        _rowsStale = true;
        RefreshRows();
    }

    /// <summary>Refetches the rows of the window once after a burst of database writes (then the timer stops).</summary>
    private void RefreshRows()
    {
        _rowRefresh.Stop();
        if (!_rowsStale)
        {
            return;
        }
        _rowsStale = false;
        var (start, end) = WindowRange();
        var ids = new List<long>();
        for (var i = start; i < end && i < Refs.Count; i++)
        {
            ids.Add(Refs[i].Id);
        }
        if (ids.Count > 0)
        {
            FetchRows(ids, replace: true);
        }
    }

    // MARK: Prefetch window

    private (int Start, int End) VisibleRange() => _geometry.Indices(new GridRect(0, _offset.Y, Bounds.Width, Bounds.Height));

    private (int Start, int End) WindowRange() => _geometry.Indices(new GridRect(
        0, _offset.Y - Bounds.Height * PrefetchScreens, Bounds.Width, Bounds.Height * (1 + 2 * PrefetchScreens)));

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

    private void RequestThumbnail(Asset asset, ThumbnailPriority priority)
    {
        if (_model is null)
        {
            return;
        }
        var request = ThumbnailRequest.For(asset, _bucket);
        var key = request.Key;
        if (Thumbnails.Contains(key))
        {
            return;
        }
        if (_thumbnailTasks.TryGetValue(asset.Id, out var existing) && existing.Key == key && existing.Priority >= priority)
        {
            return;
        }
        var cancel = new CancellationTokenSource();
        _thumbnailTasks[asset.Id] = (cancel, priority, key);
        var service = _model.Thumbnails;
        _ = Load();
        // The upgraded request joins the same job before the old waiter leaves it.
        existing.Cancel?.Cancel();

        async Task Load()
        {
            var image = await service.GetAsync(request, priority, cancel.Token);
            if (cancel.IsCancellationRequested)
            {
                return;
            }
            // A tile that scrolled away and waited was loaded for the disk cache: in memory it would push out what is
            // on screen. Shown again later, it comes back from disk in a few milliseconds.
            if (_thumbnailTasks.TryGetValue(asset.Id, out var now) && now.Cancel == cancel && now.Priority == ThumbnailPriority.Background)
            {
                _thumbnailTasks.Remove(asset.Id);
                return;
            }
            var bitmap = image is null ? null : await Task.Run(() => ThumbnailCache.ToBitmap(image));
            if (cancel.IsCancellationRequested)
            {
                bitmap?.Dispose();
                return;
            }
            Thumbnails.Insert(key, bitmap);
            if (_thumbnailTasks.TryGetValue(asset.Id, out var current) && current.Cancel == cancel)
            {
                _thumbnailTasks.Remove(asset.Id);
            }
            FirstThumbnailShown?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    /// <summary>Raised whenever a thumbnail arrives (launch metrics).</summary>
    public event EventHandler? FirstThumbnailShown;

    private void CancelAllThumbnails()
    {
        foreach (var task in _thumbnailTasks.Values)
        {
            task.Cancel.Cancel();
        }
        _thumbnailTasks.Clear();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && _model is not null)
        {
            // Ctrl+wheel resizes the tiles, as in Explorer; one slider step per notch.
            if (e.Delta.Y != 0)
            {
                var side = _model.State.TileSide + Math.Sign(e.Delta.Y) * JGMetrics.TileSideStep;
                _model.SetTileSide(Math.Clamp(side, JGMetrics.TileSideMin, JGMetrics.TileSideMax));
            }
            e.Handled = true;
            return;
        }
        Offset = new Vector(0, _offset.Y - e.Delta.Y * WheelStep);
        RaiseScrollInvalidated(EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Settings.SettingsStore.Current.Changed += OnSettingsChanged;
    }

    /// <summary>Decor (crosshair marks) and the accent apply at once.</summary>
    private void OnSettingsChanged(object? sender, (Core.Settings.AppSettings Old, Core.Settings.AppSettings New) change)
    {
        if (change.Old.AccentColor != change.New.AccentColor)
        {
            // Cached text carries its color.
            _textTheme = null;
        }
        if (change.Old.CrosshairMarks != change.New.CrosshairMarks || change.Old.AccentColor != change.New.AccentColor)
        {
            InvalidateVisual();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Settings.SettingsStore.Current.Changed -= OnSettingsChanged;
        CancelAllThumbnails();
        _rowRefresh.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
