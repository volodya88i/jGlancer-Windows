using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace JGlancer.Design;

/// <summary>Peaks of an audio file for the waveform scrubber: per bucket the lowest and highest sample (−1 … 1).</summary>
public sealed record JGWaveformPeaks(float[] Lows, float[] Highs)
{
    public int Count => Math.Min(Lows.Length, Highs.Length);
}

/// <summary>The shape of the scrubber's waveform (macOS ScrubberView+Paths).</summary>
public static class JGScrubberMath
{
    /// <summary>The whole file's peaks across the width.</summary>
    public static IReadOnlyList<Point> WaveformOutline(JGWaveformPeaks peaks, Size size) =>
        WaveformOutline(peaks, size, 1, new JGTimeRange(0, 1), null);

    /// <summary>
    /// A filled outline of the peaks shown in <paramref name="shown"/> of a file of <paramref name="duration"/>, one
    /// column per pixel, scaled so the loudest peak of the whole file reaches the edges; silence still reads as a
    /// hairline. Where <paramref name="detail"/> covers a column with finer buckets, they are used instead.
    /// </summary>
    public static IReadOnlyList<Point> WaveformOutline(JGWaveformPeaks peaks, Size size, double duration, JGTimeRange shown, JGWaveformDetail? detail)
    {
        ArgumentNullException.ThrowIfNull(peaks);
        var columns = (int)Math.Floor(size.Width);
        if (peaks.Count == 0 || columns < 2 || duration <= 0 || shown.Length <= 0)
        {
            return [];
        }
        var loudest = 0.05f;
        for (var i = 0; i < peaks.Count; i++)
        {
            loudest = Math.Max(loudest, Math.Max(peaks.Highs[i], -peaks.Lows[i]));
        }
        var whole = new PeakSource(peaks, new JGTimeRange(0, duration));
        var fine = detail is null ? (PeakSource?)null : new PeakSource(detail.Peaks, detail.Range);
        var middle = size.Height / 2;
        var scale = middle / loudest;
        var floor = JGMetrics.OutlineWidth / 2;
        var tops = new double[columns];
        var bottoms = new double[columns];
        for (var column = 0; column < columns; column++)
        {
            var from = shown.Start + column / (double)columns * shown.Length;
            var to = shown.Start + (column + 1) / (double)columns * shown.Length;
            var source = fine is { } f && f.Covers(from, to) && f.BucketSeconds < whole.BucketSeconds ? f : whole;
            var (low, high) = source.Peak(from, to);
            tops[column] = middle - Math.Min(Math.Max(high * scale, floor), middle);
            bottoms[column] = middle - Math.Max(Math.Min(low * scale, -floor), -middle);
        }
        // Highs along the top, then lows back along the bottom.
        var outline = new List<Point>(columns * 2);
        for (var column = 0; column < columns; column++)
        {
            outline.Add(new Point(column, tops[column]));
        }
        for (var column = columns - 1; column >= 0; column--)
        {
            outline.Add(new Point(column, bottoms[column]));
        }
        return outline;
    }

    /// <summary>An outline as a filled shape.</summary>
    public static Geometry Fill(IReadOnlyList<Point> outline)
    {
        ArgumentNullException.ThrowIfNull(outline);
        var geometry = new StreamGeometry();
        if (outline.Count == 0)
        {
            return geometry;
        }
        using var context = geometry.Open();
        context.BeginFigure(outline[0], isFilled: true);
        for (var i = 1; i < outline.Count; i++)
        {
            context.LineTo(outline[i]);
        }
        context.EndFigure(isClosed: true);
        return geometry;
    }

    /// <summary>Peaks spread evenly over a time range.</summary>
    private readonly record struct PeakSource(JGWaveformPeaks Peaks, JGTimeRange Range)
    {
        public double BucketSeconds => Range.Length / Math.Max(Peaks.Count, 1);

        public bool Covers(double from, double to) => from >= Range.Start && to <= Range.End;

        /// <summary>The lowest and highest sample between <paramref name="from"/> and <paramref name="to"/> (at least one bucket).</summary>
        public (float Low, float High) Peak(double from, double to)
        {
            var count = Peaks.Count;
            if (count == 0 || Range.Length <= 0)
            {
                return (0, 0);
            }
            var first = Math.Clamp((int)Math.Floor((from - Range.Start) / Range.Length * count), 0, count - 1);
            var last = Math.Clamp((int)Math.Ceiling((to - Range.Start) / Range.Length * count), first + 1, count);
            var low = 0f;
            var high = 0f;
            for (var i = first; i < last; i++)
            {
                low = Math.Min(low, Peaks.Lows[i]);
                high = Math.Max(high, Peaks.Highs[i]);
            }
            return (low, high);
        }
    }
}

/// <summary>
/// The playback scrubber (DESIGN.md §6, macOS JGScrubber): a plain slider (video, W-98) or a waveform (audio) with a
/// thin <c>text.primary</c> playhead, the played part brighter. Click or drag to
/// seek (<see cref="Seek"/>: not final while dragging, final on release); keyboard seeking belongs to the player.
/// With a <see cref="Segment"/> (D-73, D-74) accent brackets mark it, the rest is dimmed, its ends drag, and the
/// timeline zooms (Ctrl+wheel around the pointer) and scrolls (Shift+wheel, horizontal wheel), following the playhead.
/// Moving the playhead only redraws this control; the track shape is cached per window and size.
/// </summary>
public sealed partial class JGScrubber : Control
{
    public static readonly StyledProperty<double> DurationProperty = AvaloniaProperty.Register<JGScrubber, double>(nameof(Duration));

    public static readonly StyledProperty<JGWaveformPeaks?> WaveformProperty =
        AvaloniaProperty.Register<JGScrubber, JGWaveformPeaks?>(nameof(Waveform));

    public static readonly StyledProperty<JGWaveformDetail?> DetailProperty =
        AvaloniaProperty.Register<JGScrubber, JGWaveformDetail?>(nameof(Detail));

    public static readonly StyledProperty<JGTimeRange?> SegmentProperty =
        AvaloniaProperty.Register<JGScrubber, JGTimeRange?>(nameof(Segment));

    /// <summary>Seconds of the shortest segment.</summary>
    public const double MinimumSegment = 0.1;

    private double _time;
    private bool _isScrubbing;
    private double _scrubTime;
    private JGSegmentEdge? _draggedEdge;
    private JGScrubberWindow _window;
    private Geometry? _track;
    private (JGScrubberWindow Window, Size Size, JGWaveformPeaks? Waveform, JGWaveformDetail? Detail) _trackKey;

    static JGScrubber()
    {
        FocusableProperty.OverrideDefaultValue<JGScrubber>(false);
        AffectsRender<JGScrubber>(DurationProperty, WaveformProperty, DetailProperty, SegmentProperty);
    }

    public JGScrubber()
    {
        _window = new JGScrubberWindow(0);
    }

    public double Duration
    {
        get => GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public JGWaveformPeaks? Waveform
    {
        get => GetValue(WaveformProperty);
        set => SetValue(WaveformProperty, value);
    }

    /// <summary>Finer peaks of the zoomed-in part (audio).</summary>
    public JGWaveformDetail? Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    /// <summary>The selected segment; null leaves segment mode (and shows the whole timeline again).</summary>
    public JGTimeRange? Segment
    {
        get => GetValue(SegmentProperty);
        set => SetValue(SegmentProperty, value);
    }

    /// <summary>The part of the timeline shown.</summary>
    public JGTimeRange Shown => _window.Range;

    /// <summary>The playhead's time; ignored while the user drags (the drag position wins).</summary>
    public double Time
    {
        get => _time;
        set
        {
            if (value == _time)
            {
                return;
            }
            var previous = _time;
            var drawn = PlayingTime;
            _time = value;
            Anchor(value, drawn);
            RequestFrame();
            if (!_isScrubbing)
            {
                // Zoomed in while playing: when the playhead runs past the right edge, the next page shows.
                if (_window.IsZoomed && previous <= _window.End && value > _window.End && value - previous < _window.Length)
                {
                    _window.Page(value);
                    WindowChanged?.Invoke(_window.Range);
                }
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// The end bracket also moves the playhead while dragged (the video player shows the frame under it). The start
    /// bracket always does.
    /// </summary>
    public bool EndEdgeMovesPlayhead { get; set; }

    /// <summary>
    /// Alt held while a bracket is dragged: the playhead stays where it is (the player does not follow the bracket).
    /// </summary>
    public bool KeepsPlayhead { get; private set; }

    /// <summary>
    /// Where the playhead is drawn: under the pointer while scrubbing, on the dragged bracket that moves it (drawn
    /// there at once, not after the player reports the seek, so it never lags or stays behind), else the player's time.
    /// </summary>
    private double ShownTime => _isScrubbing ? _scrubTime
        : KeepsPlayhead ? PlayingTime
        : _draggedEdge == JGSegmentEdge.Start && Segment is { } start ? start.Start
        : _draggedEdge == JGSegmentEdge.End && EndEdgeMovesPlayhead && Segment is { } end ? end.End
        : PlayingTime;

    /// <summary>The user scrubbed to a time; <c>isFinal</c> on release.</summary>
    public event Action<double, bool>? Seek;

    /// <summary>An end of the segment was dragged: the new segment, the end, whether the drag is over.</summary>
    public event Action<JGTimeRange, JGSegmentEdge, bool>? SegmentChanged;

    /// <summary>The shown part of the timeline changed (zoom, scroll, paging).</summary>
    public event Action<JGTimeRange>? WindowChanged;

    /// <summary>A right click inside the segment (its menu, at the pointer).</summary>
    public event Action<PointerPressedEventArgs>? SegmentMenuRequested;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WaveformProperty)
        {
            WaveformChanged(Waveform);
        }
        if (change.Property == DurationProperty || (change.Property == SegmentProperty && Segment is null && _window.IsZoomed))
        {
            _window = new JGScrubberWindow(Duration);
            WindowChanged?.Invoke(_window.Range);
        }
    }

    private double Usable => Math.Max(1, Bounds.Width - JGMetrics.SelectionEdgeWidth);

    private double X(double time) => Duration > 0 ? _window.X(time, Usable) : 0;

    private double TimeAt(double x) => _window.Time(x, Usable);

    private Geometry Track(JGWaveformPeaks waveform)
    {
        var key = (_window, Bounds.Size, Waveform, Detail);
        if (_track is null || key != _trackKey)
        {
            _trackKey = key;
            _track = WaveformTrack(waveform);
        }
        return _track;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        // Avalonia hit-tests what is drawn: the whole strip takes clicks.
        context.FillRectangle(Brushes.Transparent, bounds);
        if (_window.Duration != Duration)
        {
            _window = new JGScrubberWindow(Duration);
        }
        var edge = JGMetrics.SelectionEdgeWidth;
        if (Segment is { } tint && Duration > 0 && this.Brush(JGColor.Accent) is ISolidColorBrush accentTint)
        {
            using (context.PushClip(bounds))
            {
                context.FillRectangle(new SolidColorBrush(accentTint.Color, JGMetrics.SegmentTintOpacity),
                    new Rect(X(tint.Start), 0, Math.Max(0, X(tint.End) + edge - X(tint.Start)), bounds.Height));
            }
        }
        var x = Duration > 0 ? X(ShownTime) : 0;
        if (Waveform is { } waveform)
        {
            // A waveform not played yet: light and partly clear, so it blends with the backdrop.
            var track = Track(waveform);
            if (this.Brush(JGColor.TextPrimary) is ISolidColorBrush light)
            {
                context.DrawGeometry(new SolidColorBrush(light.Color, JGMetrics.WaveformUnplayedOpacity), null, track);
            }
            using (context.PushClip(new Rect(0, 0, Math.Clamp(x + edge / 2, 0, bounds.Width), bounds.Height)))
            {
                context.DrawGeometry(this.Brush(JGColor.TextPrimary), null, track);
            }
        }
        else
        {
            JGSliderTrack.Draw(context, this, bounds.Size, Duration > 0 ? (x + edge / 2) / bounds.Width : 0);
        }
        if (Duration <= 0)
        {
            return;
        }
        if (Segment is { } segment)
        {
            DrawSegment(context, segment, bounds);
        }
        if (x < -edge || x > bounds.Width)
        {
            return;
        }
        if (Waveform is not null)
        {
            context.FillRectangle(this.Brush(JGColor.TextPrimary), new Rect(x, 0, edge, bounds.Height));
        }
    }

    /// <summary>The brackets: <c>[</c> at the start, <c>]</c> at the end; the timeline outside dimmed.</summary>
    private void DrawSegment(DrawingContext context, JGTimeRange segment, Rect bounds)
    {
        var edge = JGMetrics.SelectionEdgeWidth;
        var serif = JGMetrics.SegmentBracketSerif;
        var height = bounds.Height;
        var startX = X(segment.Start);
        var endX = X(segment.End) + edge;
        using (context.PushClip(bounds))
        {
            if (this.Brush(JGColor.BgMedia) is ISolidColorBrush media)
            {
                var outside = new SolidColorBrush(media.Color, JGMetrics.SegmentOutsideOpacity);
                context.FillRectangle(outside, new Rect(-edge, 0, Math.Max(startX + edge, 0), height));
                context.FillRectangle(outside, new Rect(endX, 0, Math.Max(bounds.Width - endX + edge, 0), height));
            }
            var accent = this.Brush(JGColor.Accent);
            context.FillRectangle(accent, new Rect(startX, 0, edge, height));
            context.FillRectangle(accent, new Rect(startX, 0, serif, edge));
            context.FillRectangle(accent, new Rect(startX, height - edge, serif, edge));
            context.FillRectangle(accent, new Rect(endX - edge, 0, edge, height));
            context.FillRectangle(accent, new Rect(endX - serif, 0, serif, edge));
            context.FillRectangle(accent, new Rect(endX - serif, height - edge, serif, edge));
        }
    }

    // MARK: Input

    /// <summary>The segment end within reach of <paramref name="x"/>, the nearer one when both are.</summary>
    private JGSegmentEdge? EdgeNear(double x)
    {
        if (Segment is not { } segment || Duration <= 0)
        {
            return null;
        }
        var toStart = Math.Abs(x - X(segment.Start));
        var toEnd = Math.Abs(x - X(segment.End) - JGMetrics.SelectionEdgeWidth);
        if (Math.Min(toStart, toEnd) > JGMetrics.SegmentEdgeReach)
        {
            return null;
        }
        return toStart < toEnd || (toStart == toEnd && x < X(segment.Start)) ? JGSegmentEdge.Start : JGSegmentEdge.End;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (Duration <= 0)
        {
            return;
        }
        if (point.Properties.IsRightButtonPressed)
        {
            if (Segment is { } segment && segment.Contains(TimeAt(point.Position.X)))
            {
                SegmentMenuRequested?.Invoke(e);
                e.Handled = true;
            }
            return;
        }
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }
        e.Pointer.Capture(this);
        e.Handled = true;
        if (EdgeNear(point.Position.X) is { } edge)
        {
            _draggedEdge = edge;
            KeepsPlayhead = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            DragEdge(point.Position.X, isFinal: false);
            return;
        }
        _isScrubbing = true;
        _scrubTime = TimeAt(point.Position.X);
        Seek?.Invoke(_scrubTime, false);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var x = e.GetPosition(this).X;
        if (_draggedEdge is not null)
        {
            KeepsPlayhead = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            DragEdge(x, isFinal: false);
        }
        else if (_isScrubbing)
        {
            _scrubTime = TimeAt(x);
            Seek?.Invoke(_scrubTime, false);
            InvalidateVisual();
        }
        else
        {
            Cursor = EdgeNear(x) is null ? null : new Cursor(StandardCursorType.SizeWestEast);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var x = e.GetPosition(this).X;
        e.Pointer.Capture(null);
        Finish(x);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        Finish(null);
    }

    private void Finish(double? x)
    {
        if (_draggedEdge is not null)
        {
            DragEdge(x, isFinal: true);
            _draggedEdge = null;
        }
        else if (_isScrubbing)
        {
            _isScrubbing = false;
            if (x is { } at)
            {
                _scrubTime = TimeAt(at);
            }
            _time = _scrubTime;
            Anchor(_time, _time);
            Seek?.Invoke(_scrubTime, true);
            InvalidateVisual();
        }
    }

    private void DragEdge(double? x, bool isFinal)
    {
        if (_draggedEdge is not { } edge || Segment is not { } current)
        {
            return;
        }
        var moved = current;
        if (x is { } at)
        {
            var time = TimeAt(at);
            var gap = Math.Min(MinimumSegment, Duration);
            moved = edge == JGSegmentEdge.Start
                ? new JGTimeRange(Math.Clamp(time, 0, Math.Max(0, current.End - gap)), current.End)
                : new JGTimeRange(current.Start, Math.Clamp(time, Math.Min(current.Start + gap, Duration), Duration));
        }
        Segment = moved;
        SegmentChanged?.Invoke(moved, edge, isFinal);
    }

    /// <summary>In segment mode the wheel alone zooms the timeline (Settings → Playback), not only with Ctrl.</summary>
    public bool WheelZoomsAlone { get; set; }

    /// <summary>
    /// In segment mode: Ctrl+wheel zooms around the pointer (up = in), Shift+wheel or a horizontal wheel scrolls the
    /// timeline. Otherwise the wheel goes on to the views behind.
    /// </summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Segment is null || Duration <= 0 || Bounds.Width <= 0)
        {
            return;
        }
        var before = _window;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control) || (WheelZoomsAlone && !shift && Math.Abs(e.Delta.Y) >= Math.Abs(e.Delta.X));
        if (control && e.Delta.Y != 0)
        {
            var anchor = TimeAt(e.GetPosition(this).X);
            _window.Zoom(Math.Exp(e.Delta.Y * JGMetrics.WheelLinePoints * JGMetrics.ZoomPerPoint * 3), anchor);
        }
        else if (shift || Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y))
        {
            var delta = Math.Abs(e.Delta.X) >= Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
            _window.Scroll(-delta * JGMetrics.WheelLinePoints * 3 / Bounds.Width * _window.Length);
        }
        else
        {
            return;
        }
        e.Handled = true;
        if (_window != before)
        {
            WindowChanged?.Invoke(_window.Range);
            InvalidateVisual();
        }
    }
}
