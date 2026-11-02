using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace JGlancer.Design;

/// <summary>
/// The waveform's entrance (W-82): when another waveform comes (the next song, or a flat line while it
/// loads), the shape flows from what was drawn into the new one as a wave running left to right — each column a little
/// later than the one before, with a soft overshoot — instead of jumping. Off while motion is reduced.
/// </summary>
public sealed partial class JGScrubber
{
    /// <summary>A waveform of silence: a hairline while a song's peaks are being read.</summary>
    public static JGWaveformPeaks Flat { get; } = new([0], [0]);

    /// <summary>Whether the app allows motion (Settings → Reduce motion, Windows' animation setting).</summary>
    public static Func<bool> MotionAllowed { get; set; } = () => true;

    // Distance of the top and bottom edge from the middle, per column, as last drawn.
    private double[] _shownTops = [];
    private double[] _shownBottoms = [];
    private double[]? _fromTops;
    private double[]? _fromBottoms;
    private long _revealStart;
    private bool _revealing;
    private bool _revealFrameRequested;

    /// <summary>Another waveform: starts the wave from the shape on screen.</summary>
    private void WaveformChanged(JGWaveformPeaks? waveform)
    {
        if (waveform is null)
        {
            // The plain slider (video): the next song rises from a line, not from a stale shape.
            _shownTops = [];
            _shownBottoms = [];
            _revealing = false;
            return;
        }
        if (_shownTops.Length > 0 && MotionAllowed())
        {
            // From what is on screen now (also halfway through another change).
            _fromTops = _shownTops;
            _fromBottoms = _shownBottoms;
            _revealStart = Stopwatch.GetTimestamp();
            _revealing = true;
            _track = null;
            RequestRevealFrame();
        }
    }

    /// <summary>The waveform's shape now: the target, or on its way to it.</summary>
    private Geometry WaveformTrack(JGWaveformPeaks waveform)
    {
        var outline = JGScrubberMath.WaveformOutline(waveform, Bounds.Size, Duration, _window.Range, _window.IsZoomed ? Detail : null);
        var columns = outline.Count / 2;
        if (columns == 0)
        {
            return JGScrubberMath.Fill(outline);
        }
        var middle = Bounds.Height / 2;
        var tops = new double[columns];
        var bottoms = new double[columns];
        for (var column = 0; column < columns; column++)
        {
            tops[column] = middle - outline[column].Y;
            bottoms[column] = outline[outline.Count - 1 - column].Y - middle;
        }
        if (_revealing && _fromTops is { Length: > 0 } fromTops && _fromBottoms is { } fromBottoms)
        {
            var t = Stopwatch.GetElapsedTime(_revealStart).TotalSeconds / JGMetrics.WaveformRevealDuration;
            if (t >= 1 + JGMetrics.WaveformRevealSpread)
            {
                _revealing = false;
            }
            else
            {
                for (var column = 0; column < columns; column++)
                {
                    // The wave reaches this column a little after the one before it.
                    var x = columns <= 1 ? 0 : column / (double)(columns - 1);
                    var local = Math.Clamp(t - x * JGMetrics.WaveformRevealSpread, 0, 1);
                    var eased = EaseOutBack(local);
                    var source = Math.Min(fromTops.Length - 1, column * fromTops.Length / Math.Max(1, columns));
                    tops[column] = Math.Max(0, fromTops[source] + (tops[column] - fromTops[source]) * eased);
                    bottoms[column] = Math.Max(0, fromBottoms[source] + (bottoms[column] - fromBottoms[source]) * eased);
                }
            }
        }
        _shownTops = tops;
        _shownBottoms = bottoms;
        var points = new List<Point>(columns * 2);
        for (var column = 0; column < columns; column++)
        {
            points.Add(new Point(column, middle - Math.Min(tops[column], middle)));
        }
        for (var column = columns - 1; column >= 0; column--)
        {
            points.Add(new Point(column, middle + Math.Min(bottoms[column], middle)));
        }
        return JGScrubberMath.Fill(points);
    }

    /// <summary>Overshoots a little before settling (a soft spring).</summary>
    private static double EaseOutBack(double t)
    {
        const double Overshoot = 1.4;
        var u = t - 1;
        return 1 + (Overshoot + 1) * u * u * u + Overshoot * u * u;
    }

    private void RequestRevealFrame()
    {
        if (_revealFrameRequested || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }
        _revealFrameRequested = true;
        top.RequestAnimationFrame(_ =>
        {
            _revealFrameRequested = false;
            _track = null;
            InvalidateVisual();
            if (_revealing)
            {
                RequestRevealFrame();
            }
        });
    }
}
