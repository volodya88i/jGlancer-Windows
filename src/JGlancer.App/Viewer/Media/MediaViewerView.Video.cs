using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.Viewer.Media;

/// <summary>
/// Zoom and swipe for video, as the photo viewer: the wheel zooms around the pointer from fit up to
/// <see cref="MaxZoom"/> (mpv draws the picture itself, so it stays sharp), a drag pans while zoomed; at fit a drag
/// swipes to the next or previous video at once, with no slide (the animated swipe flickered). Ctrl+0 goes back
/// to fit; another file starts at fit.
/// </summary>
internal sealed partial class MediaViewerView
{
    private const double MaxZoom = 8;

    private double _zoom = 1;
    private double _panX;
    private double _panY;
    private bool _panning;
    private Point _panLast;
    private bool IsVideo => _model?.Kind == MediaKind.Video;

    private bool IsZoomed => _zoom > 1.0001;

    /// <summary>The picture's size at fit inside the surface (zero before the video's size is known).</summary>
    private Size FittedSize
    {
        get
        {
            if (_model?.Playback.VideoSize is not { Width: > 0, Height: > 0 } video || Surface.Bounds.Width <= 0 || Surface.Bounds.Height <= 0)
            {
                return default;
            }
            var scale = Math.Min(Surface.Bounds.Width / video.Width, Surface.Bounds.Height / video.Height);
            return new Size(video.Width * scale, video.Height * scale);
        }
    }

    /// <summary>The wheel over the picture zooms around the pointer (not over the controls, the top bar or the strip).</summary>
    private void OnVideoWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!IsVideo || e.Handled || e.Delta.Y == 0 || e.Source is not Visual source || !IsOverPicture(source))
        {
            return;
        }
        var fitted = FittedSize;
        if (fitted.Width <= 0)
        {
            return;
        }
        var next = Math.Clamp(_zoom * Math.Exp(e.Delta.Y * JGMetrics.WheelLinePoints * JGMetrics.ZoomPerPoint * 3), 1, MaxZoom);
        var pointer = e.GetPosition(Surface) - new Point(Surface.Bounds.Width / 2, Surface.Bounds.Height / 2);
        // The point under the pointer stays there: its place in the picture, in fractions of the picture's size.
        var u = (pointer.X - _panX * fitted.Width * _zoom) / (fitted.Width * _zoom);
        var v = (pointer.Y - _panY * fitted.Height * _zoom) / (fitted.Height * _zoom);
        _zoom = next;
        _panX = pointer.X / (fitted.Width * _zoom) - u;
        _panY = pointer.Y / (fitted.Height * _zoom) - v;
        ApplyView();
        e.Handled = true;
    }

    /// <summary>Starts panning a zoomed picture; true when it did (otherwise the press may start a swipe).</summary>
    private bool BeginPan(PointerPressedEventArgs e)
    {
        if (!IsVideo || !IsZoomed)
        {
            return false;
        }
        _panning = true;
        _panLast = e.GetPosition(this);
        e.Pointer.Capture(this);
        return true;
    }

    private bool MovePan(PointerEventArgs e)
    {
        if (!_panning)
        {
            return false;
        }
        var fitted = FittedSize;
        var point = e.GetPosition(this);
        if (fitted.Width > 0)
        {
            _panX += (point.X - _panLast.X) / (fitted.Width * _zoom);
            _panY += (point.Y - _panLast.Y) / (fitted.Height * _zoom);
            ApplyView();
        }
        _panLast = point;
        return true;
    }

    private bool EndPan(PointerReleasedEventArgs e)
    {
        if (!_panning)
        {
            return false;
        }
        _panning = false;
        e.Pointer.Capture(null);
        return true;
    }

    /// <summary>Back to fit (Ctrl+0, another file).</summary>
    private void ResetZoom()
    {
        if (_zoom == 1 && _panX == 0 && _panY == 0)
        {
            return;
        }
        _zoom = 1;
        _panX = _panY = 0;
        ApplyView();
    }

    /// <summary>Keeps the picture covering the view: no empty band beside a zoomed picture.</summary>
    private void ApplyView()
    {
        var fitted = FittedSize;
        if (fitted.Width > 0)
        {
            _panX = Clamp(_panX, fitted.Width * _zoom, Surface.Bounds.Width);
            _panY = Clamp(_panY, fitted.Height * _zoom, Surface.Bounds.Height);
        }
        _model?.Playback.Player.SetView(Math.Log2(_zoom), _panX, _panY);
    }

    private static double Clamp(double pan, double picture, double view)
    {
        var room = Math.Max(0, (picture - view) / 2) / picture;
        return Math.Clamp(pan, -room, room);
    }

    private bool IsOverPicture(Visual source) =>
        !ControlsPanel.IsVisualAncestorOf(source) && !TopBar.IsVisualAncestorOf(source) && !FilmstripArea.IsVisualAncestorOf(source);
}
