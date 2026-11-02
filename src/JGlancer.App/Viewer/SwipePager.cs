using System.Diagnostics;
using Avalonia.Controls;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.Viewer;

/// <summary>
/// Paging by mouse drags in the photo viewer (macOS SwipePager). Without animation a drag turns the page at once; with
/// it the photo follows the pointer, the neighbor comes in from the edge, and the release completes the page or springs
/// back over <see cref="JGMetrics.SwipeSettleDuration"/> (frames are requested only while settling).
/// </summary>
internal sealed class SwipePager
{
    private SwipeTracker _tracker = new(isAnimated: false);
    private bool _isTracking;
    private bool _isSettling;
    private double _velocity;
    private long? _lastTime;

    /// <summary>The content's horizontal offset while it follows the pointer or settles.</summary>
    public double Offset { get; private set; }

    /// <summary>The viewport width (a completed page travels this far).</summary>
    public double Width { get; set; }

    public bool IsAnimated { get; set; }

    /// <summary>Whether there is a file in a direction (+1 next, -1 previous), and the move to it.</summary>
    public Func<int, bool> CanPage { get; set; } = _ => false;

    public Action<int> Page { get; set; } = _ => { };

    /// <summary>Asks for frames (the canvas's top level).</summary>
    public Func<TopLevel?> Host { get; set; } = () => null;

    /// <summary>Raised when <see cref="Offset"/> changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The neighbor beside the content while it is off center: +1 next, -1 previous, 0 none.</summary>
    public int Neighbor
    {
        get
        {
            if (Offset == 0)
            {
                return 0;
            }
            var direction = Offset < 0 ? 1 : -1;
            return CanPage(direction) ? direction : 0;
        }
    }

    public void Begin()
    {
        if (_isSettling)
        {
            return;
        }
        _isTracking = true;
        _velocity = 0;
        _lastTime = null;
        _tracker = new SwipeTracker(IsAnimated);
    }

    /// <summary>The pointer moved the content by <paramref name="delta"/> pixels (negative: left).</summary>
    public void Move(double delta)
    {
        if (_isSettling)
        {
            return;
        }
        if (!_isTracking)
        {
            Begin();
        }
        var now = Stopwatch.GetTimestamp();
        if (_lastTime is { } last && now > last)
        {
            // Smoothed, so one jittery event does not decide a flick.
            _velocity = _velocity * 0.6 + delta / Stopwatch.GetElapsedTime(last, now).TotalSeconds * 0.4;
        }
        _lastTime = now;
        if (_tracker.Move(delta, CanPage) is { } direction)
        {
            Page(direction);
        }
        if (IsAnimated)
        {
            SetOffset(_tracker.Offset(CanPage));
        }
    }

    public void End()
    {
        if (!_isTracking || _isSettling)
        {
            return;
        }
        _isTracking = false;
        if (_tracker.End(Width, _velocity, CanPage) is not { } direction)
        {
            if (Offset != 0)
            {
                Settle(0, then: null);
            }
            return;
        }
        Settle(-direction * Width, then: () =>
        {
            Page(direction);
            SetOffset(0);
        });
    }

    /// <summary>Eases the offset to <paramref name="target"/>, then runs <paramref name="then"/>.</summary>
    private void Settle(double target, Action? then)
    {
        var host = Host();
        if (host is null || !IsAnimated)
        {
            SetOffset(target);
            then?.Invoke();
            return;
        }
        _isSettling = true;
        var from = Offset;
        var start = Stopwatch.GetTimestamp();
        void Frame(TimeSpan _)
        {
            var t = Math.Min(1, Stopwatch.GetElapsedTime(start).TotalSeconds / JGMetrics.SwipeSettleDuration);
            var eased = 1 - Math.Pow(1 - t, 3);
            SetOffset(from + (target - from) * eased);
            if (t < 1)
            {
                host.RequestAnimationFrame(Frame);
                return;
            }
            _isSettling = false;
            then?.Invoke();
        }
        host.RequestAnimationFrame(Frame);
    }

    private void SetOffset(double offset)
    {
        if (offset != Offset)
        {
            Offset = offset;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
