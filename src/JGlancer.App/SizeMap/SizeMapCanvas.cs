using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.SizeMap;

/// <summary>
/// The size map itself (macOS D-127, MAC-048): the layout is made off the UI thread and drawn once into a picture for
/// the size; hover and selection are drawn over it, so moving the pointer redraws only two outlines. A click selects
/// the file in the library, a double-click opens it, a double-click on a folder shows that folder.
/// <para>
/// A tree and the layout made for it stay together (macOS MAC-049): a new tree shows once its layout is ready, and
/// until then hover, clicks and the selection use the tree on screen.
/// </para>
/// </summary>
internal sealed class SizeMapCanvas : Control, IDisposable
{
    private static readonly Typeface TextFace = new(JGFonts.Text);

    private SizeTree _tree = SizeTree.Empty;
    private Shown _shown = Shown.Nothing;
    private RenderTargetBitmap? _picture;
    private Size _renderedSize;
    private double _renderedScale;
    private ThemeVariant? _renderedTheme;
    private CancellationTokenSource? _layoutWork;
    private int? _hovered;
    private IReadOnlySet<long> _selected = new HashSet<long>();
    private StreamGeometry? _selectionOutline;

    public SizeMapCanvas()
    {
        ClipToBounds = true;
        Focusable = false;
        AutomationProperties.SetName(this, L.T("Size Map"));
        ActualThemeVariantChanged += (_, _) => Relayout();
    }

    /// <summary>The node under the pointer changed (null: none), from the tree on screen.</summary>
    public event EventHandler<SizeTree.Node?>? HoverChanged;

    /// <summary>A file was clicked (its asset id).</summary>
    public event EventHandler<long>? FileClicked;

    /// <summary>A file (false) or a folder (true) was double-clicked, by its id.</summary>
    public event EventHandler<(bool IsFolder, long Id)>? DoubleClicked;

    public SizeTree Tree
    {
        get => _tree;
        set
        {
            if (!ReferenceEquals(_tree, value))
            {
                _tree = value;
                Relayout();
            }
        }
    }

    /// <summary>The library's selection, outlined in the accent.</summary>
    public IReadOnlySet<long> Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            _selectionOutline = null;
            InvalidateVisual();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Relayout();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Back in a window (the module moved to its other place): drawn again.
        Relayout();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Dispose();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Drops the picture and any layout being made (a window it shows in again draws it anew).</summary>
    public void Dispose()
    {
        _layoutWork?.Cancel();
        _picture?.Dispose();
        _picture = null;
    }

    /// <summary>Lays the tree out for the current size off the UI thread, then draws it.</summary>
    private void Relayout()
    {
        _layoutWork?.Cancel();
        var size = Bounds.Size;
        if (size.Width < 1 || size.Height < 1)
        {
            return;
        }
        var tree = _tree;
        var stop = new CancellationTokenSource();
        _layoutWork = stop;
        _ = Make();
        async Task Make()
        {
            try
            {
                var layout = await Task.Run(() => SizeMapLayout.Make(
                    tree, new MapRect(0, 0, size.Width, size.Height), JGMetrics.SizeMapHeaderHeight, JGMetrics.SizeMapPadding,
                    JGMetrics.SizeMapMinSide), stop.Token);
                if (stop.IsCancellationRequested)
                {
                    return;
                }
                _shown = new Shown(tree, layout, tree.FileNodes());
                SetHovered(null);
                _selectionOutline = null;
                Draw(size);
                InvalidateVisual();
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    // MARK: Picture

    private void Draw(Size size)
    {
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var pixels = new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * scale)), Math.Max(1, (int)Math.Ceiling(size.Height * scale)));
        _picture?.Dispose();
        _picture = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        _renderedSize = size;
        _renderedScale = scale;
        _renderedTheme = ActualThemeVariant;
        var isDark = ActualThemeVariant == ThemeVariant.Dark || ActualThemeVariant.InheritVariant == ThemeVariant.Dark;
        var gap = this.Brush(JGColor.BgBase);
        var folderFill = this.Brush(JGColor.BgPanel);
        var headerFill = this.Brush(JGColor.BgRaised);
        var label = this.Brush(JGColor.TextSecondary);
        var fileInk = new SolidColorBrush(SizeMapPalette.FileInk(isDark));
        var fills = new Dictionary<(MediaKind, string), IBrush>();
        using var context = _picture.CreateDrawingContext();
        context.FillRectangle(gap, new Rect(size));
        var (tree, layout) = (_shown.Tree, _shown.Layout);
        foreach (var box in layout.Boxes)
        {
            var node = tree.Nodes[box.Node];
            var rect = ToRect(box.Rect.Width > 2 && box.Rect.Height > 2 ? box.Rect.Inset(0.5, 0.5) : box.Rect);
            if (node.Kind is { } kind)
            {
                if (!fills.TryGetValue((kind, node.Extension), out var fill))
                {
                    fill = new SolidColorBrush(SizeMapPalette.FileColor(kind, node.Extension, isDark));
                    fills[(kind, node.Extension)] = fill;
                }
                context.FillRectangle(fill, rect);
                if (rect.Width >= JGMetrics.SizeMapFileLabelMinWidth && rect.Height >= JGMetrics.SizeMapHeaderHeight)
                {
                    DrawLabel(context, node.Name, rect.Deflate(new Thickness(4, 2)), fileInk, top: true);
                }
            }
            else
            {
                context.FillRectangle(folderFill, rect);
                if (box.Header is { } header)
                {
                    var band = ToRect(header.Inset(0.5, 0.5));
                    context.FillRectangle(headerFill, band);
                    DrawLabel(context, node.Name + "  " + ByteFormat.Format(node.Size), band.Deflate(new Thickness(4, 0)), label, top: false);
                }
            }
        }
    }

    private static Rect ToRect(MapRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    /// <summary>One line of <paramref name="text"/>, truncated in the middle to the rectangle's width, at its top or centred in its height.</summary>
    private static void DrawLabel(DrawingContext context, string text, Rect rect, IBrush brush, bool top)
    {
        if (rect.Width <= 8)
        {
            return;
        }
        var line = Fitted(text, rect.Width, brush);
        var y = top ? rect.Y : rect.Y + (rect.Height - line.Height) / 2;
        using (context.PushClip(rect))
        {
            context.DrawText(line, new Point(rect.X, y));
        }
    }

    private static FormattedText Line(string text, IBrush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TextFace, JGMetrics.SizeMapTextSize, brush);

    /// <summary>The text, or its start and end around an ellipsis, as much as fits the width.</summary>
    private static FormattedText Fitted(string text, double width, IBrush brush)
    {
        var whole = Line(text, brush);
        if (whole.Width <= width)
        {
            return whole;
        }
        // The longest middle cut that fits (binary search on the characters kept).
        var (low, high) = (0, text.Length - 1);
        var best = Line("…", brush);
        while (low <= high)
        {
            var kept = (low + high) / 2;
            var head = (kept + 1) / 2;
            var candidate = Line(text[..head] + "…" + text[(text.Length - (kept - head))..], brush);
            if (candidate.Width <= width)
            {
                best = candidate;
                low = kept + 1;
            }
            else
            {
                high = kept - 1;
            }
        }
        return best;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Bounds.Size;
        context.FillRectangle(Brushes.Transparent, new Rect(size));
        if (_picture is null)
        {
            return;
        }
        // While a new layout is being made (resizing), the last picture stretches to fill.
        context.DrawImage(_picture, new Rect(_picture.Size), new Rect(size));
        if (_renderedScale != (TopLevel.GetTopLevel(this)?.RenderScaling ?? _renderedScale))
        {
            // Moved to a screen with another scaling: drawn again, sharp.
            _renderedScale = 0;
            Dispatcher.UIThread.Post(Relayout);
            return;
        }
        if (_renderedSize != size || _renderedTheme != ActualThemeVariant)
        {
            return;
        }
        _selectionOutline ??= SelectionOutline();
        if (_selectionOutline is { } outline)
        {
            context.DrawGeometry(null, new Pen(this.Brush(JGColor.Accent), JGMetrics.SelectionEdgeWidth), outline);
        }
        if (_hovered is { } hovered && _shown.Layout.Rects[hovered] is { } rect)
        {
            var width = JGMetrics.OutlineWidth * 1.5;
            context.DrawRectangle(new Pen(this.Brush(JGColor.TextPrimary), width), ToRect(rect.Inset(width / 2, width / 2)));
        }
    }

    private StreamGeometry? SelectionOutline()
    {
        var geometry = new StreamGeometry();
        var any = false;
        using (var stream = geometry.Open())
        {
            foreach (var id in _selected)
            {
                if (_shown.FileNodes.TryGetValue(id, out var node) && _shown.Layout.Rects[node] is { } rect)
                {
                    var r = ToRect(rect.Inset(1, 1));
                    stream.BeginFigure(r.TopLeft, false);
                    stream.LineTo(r.TopRight);
                    stream.LineTo(r.BottomRight);
                    stream.LineTo(r.BottomLeft);
                    stream.EndFigure(true);
                    any = true;
                }
            }
        }
        return any ? geometry : null;
    }

    // MARK: Pointer

    private void SetHovered(int? node)
    {
        if (node == _hovered)
        {
            return;
        }
        _hovered = node;
        InvalidateVisual();
        HoverChanged?.Invoke(this, node is { } index ? _shown.Tree.Nodes[index] : null);
    }

    private int? NodeAt(PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        return _renderedSize == Bounds.Size ? _shown.Layout.Hit(point.X, point.Y, _shown.Tree) : null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        SetHovered(NodeAt(e));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHovered(null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || NodeAt(e) is not { } index)
        {
            return;
        }
        var node = _shown.Tree.Nodes[index];
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            DoubleClicked?.Invoke(this, (node.IsFolder, node.Id));
        }
        else if (!node.IsFolder)
        {
            FileClicked?.Invoke(this, node.Id);
        }
    }

    /// <summary>A tree with the layout made for it, and where each file's node is.</summary>
    private sealed record Shown(SizeTree Tree, SizeMapLayout Layout, IReadOnlyDictionary<long, int> FileNodes)
    {
        public static Shown Nothing { get; } = new(SizeTree.Empty, SizeMapLayout.Empty, new Dictionary<long, int>());
    }
}
