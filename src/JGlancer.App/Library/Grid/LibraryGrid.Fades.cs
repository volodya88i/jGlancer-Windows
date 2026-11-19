using System.Diagnostics;
using Avalonia.Controls;
using JGlancer.Design;
using JGlancer.Platform.Windows;

namespace JGlancer.App.Library.Grid;

/// <summary>
/// Hover fades: the outline and wash under the pointer come and go in <see cref="JGMetrics.FadeDuration"/>, ease-out. A
/// selection shows at once, so it never trails the keyboard (macOS MAC-033). Only visible tiles fade; frames are
/// requested only while a fade runs, so an idle grid draws nothing. Off when Windows animation effects are off.
/// </summary>
internal sealed partial class LibraryGrid
{
    private readonly Dictionary<long, Fade> _hoverFades = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _animating;

    private readonly record struct Fade(double From, double To, double Start);

    private double Now => _clock.Elapsed.TotalSeconds;

    private static double Value(Fade fade, double now)
    {
        var t = Math.Clamp((now - fade.Start) / JGMetrics.FadeDuration, 0, 1);
        var eased = 1 - (1 - t) * (1 - t);
        return fade.From + (fade.To - fade.From) * eased;
    }

    /// <summary>How hovered (0…1) a tile looks now.</summary>
    private double HoverAmount(long id, bool hovered, double now) =>
        _hoverFades.TryGetValue(id, out var fade) ? Value(fade, now) : hovered ? 1 : 0;

    private static void StartFade(Dictionary<long, Fade> fades, long id, double to, double now)
    {
        var from = fades.TryGetValue(id, out var running) ? Value(running, now) : 1 - to;
        fades[id] = new Fade(from, to, now);
    }

    /// <summary>The tile under the pointer changed: the old one fades out, the new one in.</summary>
    private void FadeHover(int? from, int? to)
    {
        if (!Settings.Motion.Enabled)
        {
            _hoverFades.Clear();
            return;
        }
        var now = Now;
        var refs = Refs;
        if (from is { } old && old < refs.Count)
        {
            StartFade(_hoverFades, refs[old].Id, 0, now);
        }
        if (to is { } next && next < refs.Count)
        {
            StartFade(_hoverFades, refs[next].Id, 1, now);
        }
        RunFades();
    }

    private void RunFades()
    {
        if (_animating || (_hoverFades.Count == 0 && !_reflowing))
        {
            return;
        }
        _animating = true;
        InvalidateVisual();
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ => Frame());
    }

    private void Frame()
    {
        _animating = false;
        var now = Now;
        Prune(_hoverFades, now);
        EndReflowIfDone(now);
        InvalidateVisual();
        RunFades();
    }

    private static void Prune(Dictionary<long, Fade> fades, double now)
    {
        foreach (var (id, fade) in fades.ToList())
        {
            if (now - fade.Start >= JGMetrics.FadeDuration)
            {
                fades.Remove(id);
            }
        }
    }
}
