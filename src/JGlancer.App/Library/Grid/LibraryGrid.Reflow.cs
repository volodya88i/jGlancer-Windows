using Avalonia;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.Library.Grid;

/// <summary>
/// The grid's reflow (W-69): when the tile size, the names or the order change, the tiles on screen glide
/// from where they were drawn to their new places and sizes (eased, <see cref="JGMetrics.TileReflowDuration"/>);
/// tiles that come in from off screen fade in, those that leave slide out. Each change starts from what is drawn at
/// that moment, so dragging the size slider follows smoothly. Settings → Appearance (Beauty), off with Reduce motion.
/// Only the drawing moves: clicks and the keyboard use the new layout at once.
/// An image sequence unfolding (W-81) sends its frames out of the stack one after another (each a little later);
/// folding draws them as ghosts flying back into the stack and fading.
/// </summary>
internal sealed partial class LibraryGrid
{
    // Where each tile was drawn in the last frame (screen coordinates): the start of the next reflow.
    private readonly Dictionary<long, Rect> _drawnFrames = [];
    private Dictionary<long, Rect> _reflowFrom = [];
    private Dictionary<long, double> _reflowDelays = [];
    private List<(AssetRef Ref, Rect From, long Into)> _ghosts = [];
    private double _reflowStart;
    private double _reflowLength;
    private bool _reflowing;

    private static bool ReflowEnabled => Settings.Motion.Enabled && Settings.SettingsStore.Current.Settings.GridAnimation;

    /// <summary>Starts a reflow from what is on screen now (call right after the layout changed).</summary>
    private void StartReflow(Dictionary<long, Rect> from, Dictionary<long, double>? delays = null, List<(AssetRef, Rect, long)>? ghosts = null)
    {
        if (!ReflowEnabled || from.Count == 0)
        {
            return;
        }
        _reflowFrom = from;
        _reflowDelays = delays ?? [];
        _ghosts = ghosts ?? [];
        _reflowStart = Now;
        _reflowLength = JGMetrics.TileReflowDuration + (_reflowDelays.Count > 0 ? _reflowDelays.Values.Max() : 0);
        _reflowing = true;
        RunFades();
    }

    /// <summary>A copy of where the tiles are drawn now, before the layout changes.</summary>
    private Dictionary<long, Rect> DrawnNow() => ReflowEnabled ? new Dictionary<long, Rect>(_drawnFrames) : [];

    /// <summary>A tile's progress (eased 0…1) after its own delay; 1 when no reflow runs.</summary>
    private double ReflowProgress(long id, double now)
    {
        if (!_reflowing)
        {
            return 1;
        }
        var delay = _reflowDelays.GetValueOrDefault(id);
        var t = Math.Clamp((now - _reflowStart - delay) / JGMetrics.TileReflowDuration, 0, 1);
        // Ease out (cubic): fast at first, settling softly.
        return 1 - Math.Pow(1 - t, 3);
    }

    /// <summary>Where a tile is drawn now, and how opaque: on its way from its old place, or fading in.</summary>
    private (Rect Frame, double Opacity) Reflowed(long id, Rect target, double now)
    {
        var progress = ReflowProgress(id, now);
        if (progress >= 1)
        {
            return (target, 1);
        }
        if (!_reflowFrom.TryGetValue(id, out var from))
        {
            return (target, progress);
        }
        return (Lerp(from, target, progress), 1);
    }

    private static Rect Lerp(Rect from, Rect to, double progress)
    {
        double Value(double a, double b) => a + (b - a) * progress;
        return new Rect(Value(from.X, to.X), Value(from.Y, to.Y), Value(from.Width, to.Width), Value(from.Height, to.Height));
    }

    /// <summary>Tiles drawn before the reflow that are now off screen: drawn sliding out until it ends.</summary>
    private IEnumerable<int> LeavingTiles(int start, int end)
    {
        if (!_reflowing || _model is null)
        {
            yield break;
        }
        foreach (var id in _reflowFrom.Keys)
        {
            if (_model.Order.IndexOf(id) is { } index && (index < start || index >= end))
            {
                yield return index;
            }
        }
    }

    /// <summary>Frames of a folding sequence: from where they were into the stack's tile, fading out.</summary>
    private IEnumerable<(AssetRef Ref, Rect Frame, double Opacity)> Ghosts(double now)
    {
        if (!_reflowing || _model is null)
        {
            yield break;
        }
        foreach (var (item, from, into) in _ghosts)
        {
            if (_model.Order.IndexOf(into) is not { } index)
            {
                continue;
            }
            var frame = _geometry.Frame(index);
            var target = new Rect(frame.X, frame.Y - _offset.Y, frame.Width, frame.Height);
            var progress = ReflowProgress(item.Id, now);
            yield return (item, Lerp(from, target, progress), 1 - progress);
        }
    }

    /// <summary>The reflow ended (animation frame).</summary>
    private void EndReflowIfDone(double now)
    {
        if (_reflowing && now - _reflowStart >= _reflowLength)
        {
            _reflowing = false;
            _reflowFrom = [];
            _reflowDelays = [];
            _ghosts = [];
        }
    }

    /// <summary>
    /// A sequence folded or unfolded (W-81): its frames fly out of the stack in frame order, or back into it.
    /// <paramref name="drawn"/> is what was on screen before.
    /// </summary>
    private void AnimateSequence(SequenceToggle toggle, Dictionary<long, Rect> drawn)
    {
        var head = toggle.Sequence.Head;
        if (!drawn.TryGetValue(head, out var stack))
        {
            StartReflow(drawn);
            return;
        }
        var delays = new Dictionary<long, double>();
        var frames = toggle.Sequence.Frames;
        for (var i = 1; i < frames.Count; i++)
        {
            delays[frames[i]] = Math.Min(i * JGMetrics.SequenceFrameStagger, JGMetrics.SequenceStaggerLimit);
        }
        if (toggle.Expanding)
        {
            foreach (var frame in frames.Skip(1))
            {
                drawn[frame] = stack;
            }
            StartReflow(drawn, delays);
            return;
        }
        var ghosts = new List<(AssetRef, Rect, long)>();
        foreach (var frame in frames.Skip(1))
        {
            if (drawn.TryGetValue(frame, out var at))
            {
                ghosts.Add((new AssetRef(frame, MediaKind.Photo), at, head));
            }
        }
        StartReflow(drawn, delays, ghosts);
    }
}
