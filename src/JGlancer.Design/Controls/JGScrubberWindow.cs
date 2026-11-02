namespace JGlancer.Design;

/// <summary>A span of time on a scrubber, in seconds.</summary>
public readonly record struct JGTimeRange(double Start, double End)
{
    public double Length => End - Start;

    public bool Contains(double time) => time >= Start && time <= End;
}

/// <summary>An end of a selected segment.</summary>
public enum JGSegmentEdge
{
    Start,
    End,
}

/// <summary>Finer peaks for part of the timeline, drawn instead of the whole-file peaks while zoomed into that part.</summary>
public sealed record JGWaveformDetail(JGTimeRange Range, JGWaveformPeaks Peaks);

/// <summary>
/// The part of the timeline a scrubber shows (macOS ScrubberWindow): all of it, or a window the user zooms and
/// scrolls while selecting a segment (D-73).
/// </summary>
public struct JGScrubberWindow : IEquatable<JGScrubberWindow>
{
    /// <summary>The shortest window, in seconds (or the whole file when it is shorter).</summary>
    public const double MinimumLength = 0.5;

    public JGScrubberWindow(double duration)
    {
        Duration = Math.Max(duration, 0);
        Start = 0;
        Length = Duration;
    }

    public double Duration { get; private set; }

    public double Start { get; private set; }

    public double Length { get; private set; }

    public readonly double End => Start + Length;

    public readonly bool IsZoomed => Length < Duration - 0.000_001;

    public readonly JGTimeRange Range => new(Start, End);

    /// <summary>The x of <paramref name="time"/> in <paramref name="width"/>; outside 0…width when outside the window.</summary>
    public readonly double X(double time, double width) => Length > 0 ? (time - Start) / Length * width : 0;

    /// <summary>The time at <paramref name="x"/>, within the file.</summary>
    public readonly double Time(double x, double width) =>
        width > 0 ? Math.Clamp(Start + x / width * Length, 0, Duration) : Start;

    /// <summary>Zooms in (<paramref name="factor"/> &gt; 1) or out keeping <paramref name="anchor"/> in place on screen.</summary>
    public void Zoom(double factor, double anchor)
    {
        if (Duration <= 0 || factor <= 0 || !double.IsFinite(factor))
        {
            return;
        }
        var fraction = Length > 0 ? (anchor - Start) / Length : 0;
        Length = Math.Clamp(Length / factor, Math.Min(MinimumLength, Duration), Duration);
        Start = anchor - fraction * Length;
        ClampStart();
    }

    public void Scroll(double seconds)
    {
        if (double.IsFinite(seconds))
        {
            Start += seconds;
            ClampStart();
        }
    }

    /// <summary>Moves the window so <paramref name="time"/> shows near its left edge (the playhead ran past the right).</summary>
    public void Page(double time)
    {
        Start = time - Length * 0.1;
        ClampStart();
    }

    private void ClampStart() => Start = Math.Clamp(Start, 0, Math.Max(Duration - Length, 0));

    public readonly bool Equals(JGScrubberWindow other) => Duration == other.Duration && Start == other.Start && Length == other.Length;

    public override readonly bool Equals(object? obj) => obj is JGScrubberWindow other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Duration, Start, Length);

    public static bool operator ==(JGScrubberWindow left, JGScrubberWindow right) => left.Equals(right);

    public static bool operator !=(JGScrubberWindow left, JGScrubberWindow right) => !left.Equals(right);
}
