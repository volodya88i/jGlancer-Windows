using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using JGlancer.Core.Model;
using JGlancer.Design;
using JGlancer.Media.Thumbnails;

namespace JGlancer.App.Library.Grid;

/// <summary>Tile drawing (DESIGN.md §8, macOS ThumbnailTileView), all from tokens.</summary>
internal sealed partial class LibraryGrid
{
    private static readonly Typeface TextFace = new(JGFonts.Text);
    private static readonly Typeface MonoFace = new(JGFonts.Mono);
    private readonly Dictionary<(string Text, int Width), string> _truncated = [];
    private readonly Dictionary<(string Text, bool Selected, int Width), FormattedText> _captions = [];
    private readonly Dictionary<string, FormattedText> _badges = [];
    private Avalonia.Styling.ThemeVariant? _textTheme;

    /// <summary>Frames drawn and the time spent building them (benchmarks).</summary>
    public (long Frames, double Milliseconds) RenderStatistics => (_renderFrames, TimeSpan.FromTicks(_renderTicks).TotalMilliseconds);

    private long _renderFrames;
    private long _renderTicks;

    public override void Render(DrawingContext context)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            RenderTiles(context);
        }
        finally
        {
            _renderFrames++;
            _renderTicks += System.Diagnostics.Stopwatch.GetElapsedTime(started).Ticks;
        }
    }

    private void RenderTiles(DrawingContext context)
    {
        // Avalonia hit-tests what is drawn: the gaps between tiles and the empty space below them must take the
        // pointer too, or a marquee could never start there.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (_model is null || _geometry.ItemCount == 0)
        {
            return;
        }
        if (_textTheme != ActualThemeVariant)
        {
            // Cached text carries its color.
            _textTheme = ActualThemeVariant;
            _captions.Clear();
            _badges.Clear();
        }
        var colors = new TileColors(this);
        var refs = Refs;
        var (start, end) = VisibleRange();
        var now = Now;
        _sequenceBadges.Clear();
        _drawnFrames.Clear();
        void Draw(int index)
        {
            var frame = _geometry.Frame(index);
            var target = new Rect(frame.X, frame.Y - _offset.Y, frame.Width, frame.Height);
            var (drawn, opacity) = Reflowed(refs[index].Id, target, now);
            if (drawn.Bottom < 0 || drawn.Y > Bounds.Height)
            {
                return;
            }
            _drawnFrames[refs[index].Id] = drawn;
            Faded(context, opacity, () => DrawTile(context, colors, refs[index], drawn, index == _hoverIndex, now));
        }
        // Tiles leaving the screen in a reflow first, under the ones arriving.
        // Frames of a folding sequence fly into their stack, under everything else.
        foreach (var (ghost, frame, opacity) in Ghosts(now).ToList())
        {
            Faded(context, opacity, () => DrawTile(context, colors, ghost, frame, hovered: false, now));
        }
        foreach (var index in LeavingTiles(start, end).ToList())
        {
            if (index < refs.Count)
            {
                Draw(index);
            }
        }
        for (var index = start; index < end && index < refs.Count; index++)
        {
            Draw(index);
        }
        RecordDrawn(refs, start, end);
        DrawMarquee(context, colors);
    }

    private void DrawTile(DrawingContext context, TileColors colors, AssetRef item, Rect frame, bool hovered, double now)
    {
        // The drawn size (a reflow changes it); captions keep the layout's width, so their text is laid out once.
        var side = frame.Width;
        var captionWidth = (int)_geometry.TileSide;
        var square = new Rect(frame.X, frame.Y, side, side);
        var selection = SelectionAmount(item.Id, _model!.Selection.Contains(item.Id), now);
        var hover = HoverAmount(item.Id, hovered, now);
        var selected = selection >= 0.5;
        _rows.TryGetValue(item.Id, out var asset);
        Bitmap? bitmap = null;
        var known = asset is not null && Thumbnails.TryGet(ThumbnailRequest.For(asset, _bucket).Key, out bitmap);
        if (asset is not null && !known)
        {
            RepairMissing(asset);
            // Until the new size's thumbnail arrives, another size already in memory stands in (no blank tiles while
            // the size changes).
            foreach (var other in Enum.GetValues<ThumbnailBucket>())
            {
                if (other != _bucket && Thumbnails.TryGet(ThumbnailRequest.For(asset, other).Key, out var stand) && stand is not null)
                {
                    bitmap = stand;
                    break;
                }
            }
        }
        var radius = JGMetrics.RadiusSmall;
        // An image sequence (W-81): folded, two cards lie askew under its first frame.
        var sequence = _model.SequenceOf(item.Id);
        var folded = sequence is not null && _model.IsFolded(item.Id);
        if (folded)
        {
            // The face moves to the bottom-left and two cards show behind it toward the top-right, inside the tile.
            var step = side * JGMetrics.SequenceCardOffset;
            square = new Rect(square.X, square.Y + 2 * step, square.Width - 2 * step, square.Height - 2 * step);
            DrawStackCards(context, colors, bitmap is null ? square : AspectFit(bitmap.Size, square), step);
        }

        // Background: raised behind a placeholder or under the pointer, the dim accent over it as the tile is selected.
        Faded(context, bitmap is null ? 1 : hover, () => context.DrawRectangle(colors.Raised, null, square, radius, radius));
        Faded(context, selection, () => context.DrawRectangle(colors.AccentDim, null, square, radius, radius));
        var imageRect = square;
        if (bitmap is not null)
        {
            imageRect = AspectFit(bitmap.Size, square);
            using (context.PushClip(new RoundedRect(imageRect, radius)))
            {
                context.DrawImage(bitmap, new Rect(bitmap.Size), imageRect);
            }
        }
        else if (known)
        {
            // Known to have no thumbnail: the kind's glyph (audio without cover art, unreadable files).
            DrawGlyph(context, item.Kind, square, colors.Tertiary);
        }
        // Edges: the hover outline gives way to the accent selection edge.
        Faded(context, hover * (1 - selection), () =>
        {
            var edge = JGMetrics.OutlineWidth;
            context.DrawRectangle(null, new Pen(colors.Strong, edge), square.Deflate(edge / 2), radius, radius);
        });
        Faded(context, selection, () =>
        {
            var edge = JGMetrics.SelectionEdgeWidth;
            context.DrawRectangle(null, new Pen(colors.Accent, edge), square.Deflate(edge / 2), radius, radius);
            if (Settings.SettingsStore.Current.Settings.CrosshairMarks)
            {
                DrawCrosshairs(context, colors.Accent, square);
            }
        });
        if (sequence is not null)
        {
            DrawSequenceBadge(context, colors, item.Id, sequence.Frames.Count, folded, imageRect);
        }
        if (asset?.Duration is { } duration && DurationFormat.Positional(duration) is { } text)
        {
            DrawBadge(context, colors, text, imageRect);
        }
        if (asset?.Flag is { } flag and not AssetFlag.None)
        {
            DrawFlag(context, colors, flag, imageRect);
        }
        if (CaptionsVisible && asset is not null)
        {
            var caption = Caption(asset.Name, selected, captionWidth, selected ? colors.Primary : colors.Secondary);
            // Under the tile's place, even when a folded stack drew its face smaller.
            var y = frame.Y + side + JGMetrics.BadgeInset;
            context.DrawText(caption, new Point(frame.X + (side - caption.Width) / 2, y));
        }
    }

    /// <summary>Decor (Settings → Appearance): a small "+" centered on each corner of a selected tile (macOS).</summary>
    private static void DrawCrosshairs(DrawingContext context, IBrush brush, Rect square)
    {
        var pen = new Pen(brush, JGMetrics.OutlineWidth);
        var half = JGMetrics.CrosshairSize / 2;
        foreach (var corner in new[] { square.TopLeft, square.TopRight, square.BottomLeft, square.BottomRight })
        {
            context.DrawLine(pen, new Point(corner.X - half, corner.Y), new Point(corner.X + half, corner.Y));
            context.DrawLine(pen, new Point(corner.X, corner.Y - half), new Point(corner.X, corner.Y + half));
        }
    }
    /// <summary>Draws at <paramref name="amount"/> opacity: nothing at 0, directly at 1, in an opacity layer between.</summary>
    private static void Faded(DrawingContext context, double amount, Action draw)
    {
        if (amount <= 0)
        {
            return;
        }
        if (amount >= 1)
        {
            draw();
            return;
        }
        using (context.PushOpacity(amount))
        {
            draw();
        }
    }

    /// <summary>The badges of the sequences drawn (screen coordinates): a click there folds or unfolds (W-81).</summary>
    private readonly Dictionary<long, Rect> _sequenceBadges = [];

    /// <summary>Two cards under a folded sequence's face, a step up and right each, turned a little .</summary>
    private static void DrawStackCards(DrawingContext context, TileColors colors, Rect face, double step)
    {
        var radius = JGMetrics.RadiusSmall;
        var pen = new Pen(colors.Strong, JGMetrics.OutlineWidth);
        foreach (var (shift, degrees) in new[] { (2.0, JGMetrics.SequenceCardAngle), (1.0, -JGMetrics.SequenceCardAngle / 2) })
        {
            var center = face.Center;
            var turn = Matrix.CreateTranslation(-center.X, -center.Y)
                * Matrix.CreateRotation(degrees * Math.PI / 180)
                * Matrix.CreateTranslation(center.X + shift * step, center.Y - shift * step);
            using (context.PushTransform(turn))
            {
                context.DrawRectangle(colors.Raised, pen, face, radius, radius);
            }
        }
    }

    /// <summary>
    /// The frame count with a stack glyph at the picture's bottom-right, then an arrow: › unfolds (the frames spread to
    /// the right), ‹ folds them back; an accent edge while unfolded.
    /// </summary>
    private void DrawSequenceBadge(DrawingContext context, TileColors colors, long id, int frames, bool folded, Rect imageRect)
    {
        var text = frames.ToString(CultureInfo.CurrentCulture);
        if (!_badges.TryGetValue("seq:" + text, out var formatted))
        {
            formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoFace,
                JGTypeScale.Mono.Size, colors.Primary);
            _badges["seq:" + text] = formatted;
        }
        var inset = JGMetrics.BadgeInset;
        var glyph = JGMetrics.IconSizeSmall;
        var width = inset + glyph + inset / 2 + formatted.Width + inset / 2 + glyph + inset / 2;
        var height = Math.Max(formatted.Height, glyph);
        var badge = new Rect(imageRect.Right - inset - width, imageRect.Bottom - inset - height, width, height);
        // Unfolded: an accent edge says a click folds it again.
        context.DrawRectangle(colors.Scrim, folded ? null : new Pen(colors.Accent, JGMetrics.SelectionEdgeWidth), badge, JGMetrics.BadgeRadius, JGMetrics.BadgeRadius);
        var scale = glyph / 24;
        var origin = new Point(badge.X + inset, badge.Y + (height - glyph) / 2);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(origin.X, origin.Y)))
        {
            context.DrawGeometry(null, new Pen(colors.Primary, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Glyphs.Get(JGCustomIcons.Stack));
        }
        context.DrawText(formatted, new Point(badge.X + inset + glyph + inset / 2, badge.Y + (height - formatted.Height) / 2));
        var arrow = new Point(badge.X + inset + glyph + inset / 2 + formatted.Width + inset / 2, origin.Y);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(arrow.X, arrow.Y)))
        {
            context.DrawGeometry(null, new Pen(folded ? colors.Primary : colors.Accent, 2.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
                Glyphs.Get(folded ? JGIconData.ChevronRight : JGIconData.ChevronLeft));
        }
        _sequenceBadges[id] = badge;
    }

    private void DrawBadge(DrawingContext context, TileColors colors, string text, Rect imageRect)
    {
        if (!_badges.TryGetValue(text, out var formatted))
        {
            formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoFace,
                JGTypeScale.Mono.Size, colors.Primary);
            _badges[text] = formatted;
        }
        var inset = JGMetrics.BadgeInset;
        var badge = new Rect(imageRect.X + inset, imageRect.Y + inset, formatted.Width + 2 * inset, formatted.Height);
        context.DrawRectangle(colors.Scrim, null, badge, JGMetrics.BadgeRadius, JGMetrics.BadgeRadius);
        context.DrawText(formatted, new Point(badge.X + inset, badge.Y));
    }

    /// <summary>
    /// The flag's star in the top-right corner of the picture (macOS D-64): on a badge of bg.base at 85 %, so the star
    /// keeps its contrast over any photo.
    /// </summary>
    private void DrawFlag(DrawingContext context, TileColors colors, AssetFlag flag, Rect imageRect)
    {
        var side = JGMetrics.FlagBadgeSide;
        var inset = JGMetrics.BadgeInset;
        var badge = new Rect(imageRect.Right - inset - side, imageRect.Y + inset, side, side);
        using (context.PushOpacity(JGMetrics.FlagBadgeOpacity))
        {
            context.DrawRectangle(colors.Base, null, badge, JGMetrics.BadgeRadius, JGMetrics.BadgeRadius);
        }
        var star = JGMetrics.FlagStarSize;
        FlagStyle.Draw(context, this.Brush(FlagStyle.Token(flag)), new Rect(badge.Center.X - star / 2, badge.Center.Y - star / 2, star, star));
    }

    private static void DrawGlyph(DrawingContext context, MediaKind kind, Rect square, IBrush brush)
    {
        var data = kind switch
        {
            MediaKind.Video => JGIconData.Film,
            MediaKind.Audio => JGIconData.AudioLines,
            _ => JGIconData.Image,
        };
        var size = JGMetrics.TileGlyphSize;
        var scale = size / 24;
        var origin = new Point(square.X + (square.Width - size) / 2, square.Y + (square.Height - size) / 2);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(origin.X, origin.Y)))
        {
            // A light stroke, like the macOS glyph's light weight.
            context.DrawGeometry(null, new Pen(brush, 1.25, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Glyphs.Get(data));
        }
    }

    private GridRect? _dropBar;

    /// <summary>Where dragged tiles would land in an album (content coordinates); null hides the bar.</summary>
    public GridRect? DropBar
    {
        get => _dropBar;
        set
        {
            if (_dropBar != value)
            {
                _dropBar = value;
                InvalidateVisual();
            }
        }
    }

    private void DrawMarquee(DrawingContext context, TileColors colors)
    {
        if (_dropBar is { } bar)
        {
            context.FillRectangle(colors.Accent, new Rect(bar.X, bar.Y - _offset.Y, bar.Width, bar.Height));
        }
        if (_marquee is not { } rect)
        {
            return;
        }
        var screen = new Rect(rect.X, rect.Y - _offset.Y, rect.Width, rect.Height);
        context.DrawRectangle(new ImmutableSolidColorBrush(colors.AccentDimColor, JGMetrics.MarqueeFillOpacity), new Pen(colors.Accent, JGMetrics.OutlineWidth), screen);
    }

    /// <summary>The name centred under the tile, shortened in the middle when it is too wide ("IMG_0…34.jpg").</summary>
    private FormattedText Caption(string name, bool selected, int width, IBrush brush)
    {
        var key = (name, selected, width);
        if (_captions.TryGetValue(key, out var cached))
        {
            return cached;
        }
        if (_captions.Count > 4000)
        {
            _captions.Clear();
            _truncated.Clear();
        }
        var text = Truncated(name, width);
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TextFace, JGTypeScale.Body.Size, brush);
        _captions[key] = formatted;
        return formatted;
    }

    private string Truncated(string name, int width)
    {
        if (_truncated.TryGetValue((name, width), out var cached))
        {
            return cached;
        }
        double Measure(string text) =>
            new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TextFace, JGTypeScale.Body.Size, null).Width;
        var result = name;
        if (Measure(name) > width)
        {
            // Longest middle-shortened form that fits: binary search on the number of kept characters.
            var low = 0;
            var high = name.Length - 1;
            result = "…";
            while (low <= high)
            {
                var kept = (low + high) / 2;
                var head = (kept + 1) / 2;
                var candidate = string.Concat(name.AsSpan(0, head), "…", name.AsSpan(name.Length - (kept - head)));
                if (Measure(candidate) <= width)
                {
                    result = candidate;
                    low = kept + 1;
                }
                else
                {
                    high = kept - 1;
                }
            }
        }
        _truncated[(name, width)] = result;
        return result;
    }

    internal static Rect AspectFit(Size size, Rect rect)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            return rect;
        }
        var scale = Math.Min(rect.Width / size.Width, rect.Height / size.Height);
        var fitted = new Size(Math.Round(size.Width * scale), Math.Round(size.Height * scale));
        return new Rect(rect.Center.X - fitted.Width / 2, rect.Center.Y - fitted.Height / 2, fitted.Width, fitted.Height);
    }

    /// <summary>The token brushes of one render pass.</summary>
    private readonly struct TileColors(LibraryGrid grid)
    {
        public IBrush Raised { get; } = grid.Brush(JGColor.BgRaised);
        public IBrush AccentDim { get; } = grid.Brush(JGColor.AccentDim);
        public Color AccentDimColor { get; } = (grid.Brush(JGColor.AccentDim) as ISolidColorBrush)?.Color ?? Colors.Gray;
        public IBrush Accent { get; } = grid.Brush(JGColor.Accent);
        public IBrush Strong { get; } = grid.Brush(JGColor.LineStrong);
        public IBrush Primary { get; } = grid.Brush(JGColor.TextPrimary);
        public IBrush Secondary { get; } = grid.Brush(JGColor.TextSecondary);
        public IBrush Tertiary { get; } = grid.Brush(JGColor.TextTertiary);
        public IBrush Scrim { get; } = grid.Brush(JGColor.OverlayScrim);
        public IBrush Base { get; } = grid.Brush(JGColor.BgBase);
    }

    /// <summary>Parsed glyph geometries.</summary>
    private static class Glyphs
    {
        private static readonly Dictionary<string, Geometry> Cache = [];

        public static Geometry Get(string data)
        {
            if (!Cache.TryGetValue(data, out var geometry))
            {
                geometry = StreamGeometry.Parse(data);
                Cache[data] = geometry;
            }
            return geometry;
        }
    }
}
