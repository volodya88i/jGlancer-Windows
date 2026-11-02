using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using JGlancer.App.Library;
using JGlancer.App.Library.Grid;
using JGlancer.Core.Model;
using JGlancer.Design;
using JGlancer.Media.Thumbnails;

namespace JGlancer.App.Viewer;

/// <summary>
/// The Picasa-style filmstrip of the viewers (macOS FilmstripView): the folder's files on a strip, the current one with
/// the accent edge and kept in the middle; click to switch, one file per wheel notch. Drawn as one control: only
/// visible tiles are drawn and load thumbnails (small bucket, shared memory cache); rows load lazily around them.
/// </summary>
internal sealed class Filmstrip : Control
{
    private static readonly Typeface TextFace = new(JGFonts.Text);
private static readonly Geometry PhotoGlyph = Geometry.Parse(JGIconData.Image);
    private static readonly Geometry VideoGlyph = Geometry.Parse(JGIconData.Film);
    private static readonly Geometry AudioGlyph = Geometry.Parse(JGIconData.AudioLines);

    private static Geometry Glyph(MediaKind kind) => kind switch
    {
        MediaKind.Video => VideoGlyph,
        MediaKind.Audio => AudioGlyph,
        _ => PhotoGlyph,
    };
    private readonly Dictionary<long, CancellationTokenSource> _requests = [];
    private readonly Dictionary<string, FormattedText> _names = [];
    private IFilmstripSource? _model;
    private double _scroll;
    private int _wheelCenter;
    private int? _hover;
    private long? _scrollAnimation;

    public Filmstrip()
    {
        Height = JGMetrics.FilmstripHeight;
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    public IFilmstripSource? Model
    {
        get => _model;
        set
        {
            if (_model is not null)
            {
                _model.Changed -= OnModelChanged;
                _model.RowsLoaded -= OnRowsLoaded;
            }
            CancelRequests();
            _model = value;
            if (_model is not null)
            {
                _model.Changed += OnModelChanged;
                _model.RowsLoaded += OnRowsLoaded;
                _wheelCenter = _model.Index;
            }
            ScrollTo(_wheelCenter, animated: false);
        }
    }

    /// <summary>
    /// True while the strip is shown. A hidden strip loads no rows or thumbnails: paging through a big folder would
    /// otherwise make thumbnails for every tile around each photo nobody sees.
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }
            _isActive = value;
            if (value)
            {
                ScrollTo(_wheelCenter, animated: false);
            }
            else
            {
                CancelRequests();
            }
        }
    }

    private bool _isActive;

    private static double Pitch => JGMetrics.FilmstripTileWidth + JGSpacing.Xs;

    private int Count => _model?.Count ?? 0;

    private double ContentWidth => Count == 0 ? 0 : 2 * JGSpacing.M + Count * JGMetrics.FilmstripTileWidth + (Count - 1) * JGSpacing.Xs;

    /// <summary>A short strip stays centered, like Picasa's.</summary>
    private double Lead => Math.Max(0, (Bounds.Width - ContentWidth) / 2) + JGSpacing.M;

    private double TileX(int index) => Lead + index * Pitch - _scroll;

    private int? IndexAt(Point point)
    {
        var x = point.X + _scroll - Lead;
        if (x < 0)
        {
            return null;
        }
        var index = (int)(x / Pitch);
        return index < Count && x - index * Pitch <= JGMetrics.FilmstripTileWidth ? index : null;
    }

    private int _shownIndex = -1;

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (_model is not null && _model.Index != _shownIndex)
        {
            _shownIndex = _model.Index;
            _wheelCenter = _model.Index;
            ScrollTo(_wheelCenter, animated: true);
        }
        InvalidateVisual();
    }

    private void OnRowsLoaded(object? sender, EventArgs e)
    {
        InvalidateVisual();
        RequestVisible();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ScrollTo(_wheelCenter, animated: false);
    }

    // MARK: Scrolling

    /// <summary>Scrolls so tile <paramref name="index"/> sits in the middle (clamped to the strip).</summary>
    private void ScrollTo(int index, bool animated)
    {
        // In strip coordinates (the strip starts at 0, its tiles after the leading margin).
        var middle = JGSpacing.M + index * Pitch + JGMetrics.FilmstripTileWidth / 2;
        var target = Math.Clamp(middle - Bounds.Width / 2, 0, Math.Max(0, ContentWidth - Bounds.Width));
        if (!animated || !Settings.Motion.Enabled || TopLevel.GetTopLevel(this) is not { } top)
        {
            _scroll = target;
            _scrollAnimation = null;
            InvalidateVisual();
            RequestVisible();
            return;
        }
        var from = _scroll;
        var start = Stopwatch.GetTimestamp();
        _scrollAnimation = start;
        void Frame(TimeSpan _)
        {
            if (_scrollAnimation != start)
            {
                return;
            }
            var t = Math.Min(1, Stopwatch.GetElapsedTime(start).TotalSeconds / JGMetrics.FadeDuration);
            _scroll = from + (target - from) * (1 - Math.Pow(1 - t, 3));
            InvalidateVisual();
            if (t < 1)
            {
                top.RequestAnimationFrame(Frame);
            }
            else
            {
                _scrollAnimation = null;
                RequestVisible();
            }
        }
        top.RequestAnimationFrame(Frame);
    }

    private (int Start, int End) VisibleRange()
    {
        if (Count == 0)
        {
            return (0, 0);
        }
        var start = Math.Max(0, (int)Math.Floor((_scroll - Lead) / Pitch));
        var end = Math.Min(Count, (int)Math.Ceiling((_scroll + Bounds.Width - Lead) / Pitch) + 1);
        return (start, Math.Max(start, end));
    }

    // MARK: Thumbnails

    /// <summary>Rows and small thumbnails for the visible tiles; requests for tiles that left are cancelled.</summary>
    private void RequestVisible()
    {
        if (_model is not { } model || !_isActive)
        {
            return;
        }
        var (start, end) = VisibleRange();
        if (end > start)
        {
            model.PrepareRows((start + end) / 2);
        }
        var visible = new HashSet<long>();
        for (var index = start; index < end; index++)
        {
            if (model.Item(index, ThumbnailBucket.Small) is not { } item)
            {
                continue;
            }
            var request = item.Thumbnail;
            visible.Add(request.AssetId);
            if (model.Bitmaps.Contains(request.Key) || _requests.ContainsKey(request.AssetId))
            {
                continue;
            }
            var cancel = new CancellationTokenSource();
            _requests[request.AssetId] = cancel;
            _ = Load(model, request, request.AssetId, cancel);
        }
        foreach (var (id, cancel) in _requests.Where(pair => !visible.Contains(pair.Key)).ToList())
        {
            cancel.Cancel();
            _requests.Remove(id);
        }
    }

    private async Task Load(IFilmstripSource model, ThumbnailRequest request, long id, CancellationTokenSource cancel)
    {
        var image = await model.Thumbnails.GetAsync(request, ThumbnailPriority.Visible, cancel.Token);
        if (cancel.IsCancellationRequested)
        {
            return;
        }
        var bitmap = image is null ? null : await Task.Run(() => ThumbnailCache.ToBitmap(image));
        if (cancel.IsCancellationRequested)
        {
            bitmap?.Dispose();
            return;
        }
        model.Bitmaps.Insert(request.Key, bitmap);
        _requests.Remove(id);
        InvalidateVisual();
    }

    private void CancelRequests()
    {
        foreach (var cancel in _requests.Values)
        {
            cancel.Cancel();
        }
        _requests.Clear();
    }

    // MARK: Drawing

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (_model is not { } model)
        {
            return;
        }
        var raised = this.Brush(JGColor.BgRaised);
        var accent = this.Brush(JGColor.Accent);
        var strong = this.Brush(JGColor.LineStrong);
        var tertiary = this.Brush(JGColor.TextTertiary);
        var primary = this.Brush(JGColor.TextPrimary);
        var radius = JGMetrics.RadiusSmall;
        var top = (Bounds.Height - JGMetrics.FilmstripTileHeight - JGSpacing.Xxs - NameText("Ag", primary).Height) / 2;
        var (start, end) = VisibleRange();
        for (var index = start; index < end; index++)
        {
            var tile = new Rect(TileX(index), top, JGMetrics.FilmstripTileWidth, JGMetrics.FilmstripTileHeight);
            context.DrawRectangle(raised, null, tile, radius, radius);
            var item = model.Item(index, ThumbnailBucket.Small);
            Bitmap? bitmap = null;
            var known = item is not null && model.Bitmaps.TryGet(item.Thumbnail.Key, out bitmap);
            if (bitmap is not null)
            {
                using (context.PushClip(new RoundedRect(tile, radius)))
                {
                    context.DrawImage(bitmap, new Rect(bitmap.Size), LibraryGrid.AspectFit(bitmap.Size, tile));
                }
            }
            else if (known)
            {
                DrawGlyph(context, tile, tertiary, item!.Kind);
            }
            if (index == model.Index)
            {
                var edge = JGMetrics.SelectionEdgeWidth;
                context.DrawRectangle(null, new Pen(accent, edge), tile.Deflate(edge / 2), radius, radius);
            }
            else if (index == _hover)
            {
                var edge = JGMetrics.OutlineWidth;
                context.DrawRectangle(null, new Pen(strong, edge), tile.Deflate(edge / 2), radius, radius);
            }
            if (item is not null)
            {
                var name = NameText(item.Name, primary);
                var x = tile.X + Math.Max(0, (tile.Width - name.Width) / 2);
                using (context.PushClip(new Rect(tile.X, tile.Bottom, tile.Width, Bounds.Height - tile.Bottom)))
                {
                    context.DrawText(name, new Point(x, tile.Bottom + JGSpacing.Xxs));
                }
            }
        }
    }

    private FormattedText NameText(string name, IBrush brush)
    {
        if (_names.TryGetValue(name, out var cached))
        {
            return cached;
        }
        if (_names.Count > 2000)
        {
            _names.Clear();
        }
        var text = new FormattedText(name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TextFace, JGTypeScale.Body.Size, brush)
        {
            MaxTextWidth = JGMetrics.FilmstripTileWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        _names[name] = text;
        return text;
    }

    private static void DrawGlyph(DrawingContext context, Rect tile, IBrush brush, MediaKind kind)
    {
        var size = JGMetrics.IconSize;
        var scale = size / 24;
        var origin = new Point(tile.X + (tile.Width - size) / 2, tile.Y + (tile.Height - size) / 2);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(origin.X, origin.Y)))
        {
            context.DrawGeometry(null, new Pen(brush, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Glyph(kind));
        }
    }

    // MARK: Input

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hover = IndexAt(e.GetPosition(this));
        if (hover != _hover)
        {
            _hover = hover;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && IndexAt(e.GetPosition(this)) is { } index)
        {
            _model?.MoveTo(index);
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var delta = e.Delta.Y != 0 ? e.Delta.Y : -e.Delta.X;
        if (delta == 0 || Count == 0)
        {
            return;
        }
        // One file per notch; the strip moves, the photo stays until a click.
        _wheelCenter = Math.Clamp(_wheelCenter + (delta > 0 ? -1 : 1), 0, Count - 1);
        ScrollTo(_wheelCenter, animated: true);
        e.Handled = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelRequests();
        _scrollAnimation = null;
        base.OnDetachedFromVisualTree(e);
    }
}
