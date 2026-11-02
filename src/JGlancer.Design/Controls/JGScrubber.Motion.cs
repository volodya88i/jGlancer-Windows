using System.Diagnostics;
using Avalonia.Controls;

namespace JGlancer.Design;

/// <summary>
/// A smooth playhead while playing (it moved in jerks). The player reports its time per decoded frame or audio
/// chunk, at uneven intervals; between reports the playhead runs on at <see cref="PlaybackRate"/>, redrawn once per
/// display frame. A report that differs from the drawn time is eased in over <see cref="Easing"/> instead of jumping
/// back, unless it is a real jump (a seek). Paused (rate 0) nothing runs: no frames, no CPU.
/// </summary>
public sealed partial class JGScrubber
{
    /// <summary>How far the playhead may run ahead of the last report (seconds of media at rate 1).</summary>
    private const double MaxLead = 0.25;

    /// <summary>The time a difference between the drawn and the reported time takes to fade (seconds).</summary>
    private const double Easing = 0.15;

    /// <summary>A difference beyond this is a jump (seek, another file): drawn at once.</summary>
    private const double JumpLimit = 0.5;

    private double _rate;
    private double _anchorTime;
    private long _anchorTicks;
    private double _offset;
    private bool _frameRequested;

    /// <summary>The playing speed; 0 while paused, stopped or loading (the playhead then shows the reported time).</summary>
    public double PlaybackRate
    {
        get => _rate;
        set
        {
            var rate = double.IsFinite(value) && value > 0 ? value : 0;
            if (rate == _rate)
            {
                return;
            }
            // Continue from where the playhead is drawn now.
            var drawn = PlayingTime;
            _rate = rate;
            Anchor(_time, drawn);
            RequestFrame();
            InvalidateVisual();
        }
    }

    /// <summary>The time drawn for the player's position: predicted while playing, else the reported time.</summary>
    private double PlayingTime
    {
        get
        {
            if (_rate <= 0)
            {
                return _time;
            }
            var elapsed = Stopwatch.GetElapsedTime(_anchorTicks).TotalSeconds;
            var predicted = _anchorTime + Math.Min(elapsed, MaxLead) * _rate + _offset * Math.Exp(-elapsed / Easing);
            return Duration > 0 ? Math.Clamp(predicted, 0, Duration) : predicted;
        }
    }

    /// <summary>A new reported time: predictions start from it, eased from <paramref name="drawn"/> unless that is far.</summary>
    private void Anchor(double reported, double drawn)
    {
        _anchorTime = reported;
        _anchorTicks = Stopwatch.GetTimestamp();
        _offset = _rate > 0 && Math.Abs(drawn - reported) < JumpLimit ? drawn - reported : 0;
    }

    private void RequestFrame()
    {
        if (_rate <= 0 || _frameRequested || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }
        _frameRequested = true;
        top.RequestAnimationFrame(_ =>
        {
            _frameRequested = false;
            if (_rate > 0 && IsEffectivelyVisible)
            {
                InvalidateVisual();
                RequestFrame();
            }
        });
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RequestFrame();
    }
}
