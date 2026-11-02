using System.Diagnostics;
using Avalonia.Controls;
using JGlancer.Design;
using JGlancer.Platform.Windows;

namespace JGlancer.App.Library.Grid;

/// <summary>
/// Selection and hover fades (macOS: the tile layers' border and background colors animate implicitly, so a selection
/// edge fades out after deselecting). Only visible tiles fade; frames are requested only while a fade runs, so an idle
/// grid draws nothing. Off when Windows animation effects are off.
/// </summary>
internal sealed partial class LibraryGrid
{
    private readonly Dictionary<long, Fade> _selectionFades = [];
    private readonly Dictionary<long, Fade> _hoverFades = [];
    // The visible tiles of the last frame and which of them were drawn selected (targets, not mid-fade values).
    private readonly HashSet<long> _drawnVisible = [];
    private readonly HashSet<long> _drawnSelected = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _animating;

    private readonly record struct Fade(double From, double To, double Start);

    private double Now => _clock.Elapsed.TotalSeconds;

    private static double Value(Fade fade, double now)
    {
        var t = Math.Clamp((now - fade.Start) / JGMetrics.TileFadeDuration, 0, 1);
        return fade.From + (fade.To - fade.From) * t;
    }

    /// <summary>How selected (0…1) a tile looks now.</summary>
    private double SelectionAmount(long id, bool selected, double now) =>
        _selectionFades.TryGetValue(id, out var fade) ? Value(fade, now) : selected ? 1 : 0;

    /// <summary>How hovered (0…1) a tile looks now.</summary>
    private double HoverAmount(long id, bool hovered, double now) =>
        _hoverFades.TryGetValue(id, out var fade) ? Value(fade, now) : hovered ? 1 : 0;

    private static void StartFade(Dictionary<long, Fade> fades, long id, double to, double now)
    {
        var from = fades.TryGetValue(id, out var running) ? Value(running, now) : 1 - to;
        fades[id] = new Fade(from, to, now);
    }

    /// <summary>Fades the visible tiles whose selection changed since they were last drawn.</summary>
    private void FadeSelectionChanges()
    {
        if (_model is null || !Settings.Motion.Enabled)
        {
            _selectionFades.Clear();
            return;
        }
        var now = Now;
        var selection = _model.Selection;
        foreach (var id in _drawnVisible)
        {
            var selected = selection.Contains(id);
            if (selected != _drawnSelected.Contains(id))
            {
                StartFade(_selectionFades, id, selected ? 1 : 0, now);
                if (selected)
                {
                    _drawnSelected.Add(id);
                }
                else
                {
                    _drawnSelected.Remove(id);
                }
            }
        }
        RunFades();
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

    /// <summary>Remembers what the frame drew, for the next selection change.</summary>
    private void RecordDrawn(IReadOnlyList<Core.Model.AssetRef> refs, int start, int end)
    {
        _drawnVisible.Clear();
        _drawnSelected.Clear();
        if (_model is null)
        {
            return;
        }
        for (var index = start; index < end && index < refs.Count; index++)
        {
            var id = refs[index].Id;
            _drawnVisible.Add(id);
            if (_model.Selection.Contains(id))
            {
                _drawnSelected.Add(id);
            }
        }
    }

    private void RunFades()
    {
        if (_animating || (_selectionFades.Count == 0 && _hoverFades.Count == 0 && !_reflowing))
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
        Prune(_selectionFades, now);
        Prune(_hoverFades, now);
        EndReflowIfDone(now);
        InvalidateVisual();
        RunFades();
    }

    private static void Prune(Dictionary<long, Fade> fades, double now)
    {
        foreach (var (id, fade) in fades.ToList())
        {
            if (now - fade.Start >= JGMetrics.TileFadeDuration)
            {
                fades.Remove(id);
            }
        }
    }
}
