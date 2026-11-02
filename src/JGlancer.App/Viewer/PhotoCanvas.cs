using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace JGlancer.App.Viewer;

/// <summary>Zoom commands of the photo view (Ctrl+0 fit, Ctrl+9 fill, Ctrl+1 actual size, Ctrl+= / Ctrl+-).</summary>
internal enum ZoomCommand
{
    Fit,
    Fill,
    ActualSize,
    ZoomIn,
    ZoomOut,
}

/// <summary>
/// The photo canvas (macOS PhotoScrollView): the picture centered at fit (never enlarged past 100 %), zoom with the
/// wheel around the pointer, Ctrl keys and double-click, panning by drag when zoomed; at fit a drag is a swipe and a
/// horizontal wheel turns pages (FEATURES §4.1). 100 % is one photo pixel per device pixel. View rotation in quarter
/// turns. One control, drawn directly: no image control per photo.
/// </summary>
internal sealed class PhotoCanvas : Control
{
    private const double KeyZoomStep = 1.25;
    private const double WheelZoomStep = 1.1;
    private const double MaxZoom = 8;
    // A horizontal wheel turns one page per notch, and a burst of touchpad events only one.
    private static readonly TimeSpan HorizontalPageCooldown = TimeSpan.FromMilliseconds(300);

    private ViewerPicture? _picture;
    private int _turns;
    private long? _shownId;
    private double _scale = 1;
    private Point _center;
    private bool _isFitted = true;
    private Point? _dragStart;
    private Point _dragLast;
    private bool _isSwiping;
    private bool _isPanning;
    private double _horizontalWheel;
    private long _lastHorizontalPage;

    public PhotoCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public SwipePager? Pager { get; set; }

    /// <summary>The photo <paramref name="offset"/> places away, for the swipe (null while it is not decoded).</summary>
    public Func<int, ViewerPicture?> Neighbor { get; set; } = _ => null;

    /// <summary>A horizontal wheel notch: +1 next, -1 previous.</summary>
    public event EventHandler<int>? Navigate;

    /// <summary>The zoom went past fit: the viewer decodes full resolution.</summary>
    public event EventHandler? ZoomedBeyondFit;

    /// <summary>Pointer or wheel activity (shows the HUD).</summary>
    public event EventHandler? Activity;

    /// <summary>The user zoomed or panned (the compare view follows with the other photo).</summary>
    public event EventHandler? ViewChanged;

    /// <summary>
    /// The view as fractions, to link two canvases (compare, W-77): the zoom relative to fit, and the point of the photo
    /// (0…1 of its shown size) at the middle of the canvas. Photos of different sizes then show the same part.
    /// </summary>
    public (double Zoom, double X, double Y) LinkedView
    {
        get
        {
            var size = Shown * _scale;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return (1, 0.5, 0.5);
            }
            var left = _center.X - size.Width / 2;
            var top = _center.Y - size.Height / 2;
            return (_scale / FitScale, (Bounds.Width / 2 - left) / size.Width, (Bounds.Height / 2 - top) / size.Height);
        }
    }

    /// <summary>Shows the part another canvas shows (<see cref="LinkedView"/>), without raising <see cref="ViewChanged"/>.</summary>
    public void ApplyLinkedView((double Zoom, double X, double Y) view)
    {
        var fit = FitScale;
        _scale = Math.Clamp(fit * view.Zoom, MinScale, MaxScale);
        _isFitted = Math.Abs(_scale - fit) < 0.001 * fit;
        var size = Shown * _scale;
        _center = _isFitted
            ? new Point(Bounds.Width / 2, Bounds.Height / 2)
            : new Point(Bounds.Width / 2 - (view.X - 0.5) * size.Width, Bounds.Height / 2 - (view.Y - 0.5) * size.Height);
        Constrain();
        if (_scale > fit * 1.01)
        {
            ZoomedBeyondFit?.Invoke(this, EventArgs.Empty);
        }
        InvalidateVisual();
    }

    /// <summary>Displays <paramref name="picture"/> of photo <paramref name="id"/>: a new photo or a new rotation fits;
    /// a refined picture of the same photo keeps the zoom.</summary>
    public void Display(long? id, ViewerPicture? picture, int quarterTurns)
    {
        var resets = id != _shownId || quarterTurns != _turns || FullSize(picture, quarterTurns) != FullSize(_picture, _turns);
        _shownId = id;
        _picture = picture;
        _turns = quarterTurns;
        if (resets)
        {
            Fit();
        }
        InvalidateVisual();
    }

    private double PixelScale => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

    /// <summary>The photo's full size as shown (rotation applied), in pixels.</summary>
    private static Size FullSize(ViewerPicture? picture, int turns) => picture is null
        ? default
        : turns % 2 == 0 ? new Size(picture.FullWidth, picture.FullHeight) : new Size(picture.FullHeight, picture.FullWidth);

    private Size Shown => FullSize(_picture, _turns);

    /// <summary>The scale at which the photo fits, never above 100 %.</summary>
    private double FitScale
    {
        get
        {
            var size = Shown;
            if (size.Width <= 0 || size.Height <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
            {
                return 1 / PixelScale;
            }
            return Math.Min(1 / PixelScale, Math.Min(Bounds.Width / size.Width, Bounds.Height / size.Height));
        }
    }

    private double FillScale
    {
        get
        {
            var size = Shown;
            return size.Width <= 0 || size.Height <= 0 ? FitScale : Math.Max(Bounds.Width / size.Width, Bounds.Height / size.Height);
        }
    }

    private double MinScale => FitScale / 2;

    private double MaxScale => Math.Max(MaxZoom / PixelScale, FitScale);

    /// <summary>True at fit (drags swipe, resizing refits).</summary>
    public bool IsFitted => _isFitted;

    // MARK: Zoom

    public void Zoom(ZoomCommand command)
    {
        var middle = new Point(Bounds.Width / 2, Bounds.Height / 2);
        switch (command)
        {
            case ZoomCommand.Fit:
                Fit();
                break;
            case ZoomCommand.Fill:
                SetZoom(FillScale, middle);
                break;
            case ZoomCommand.ActualSize:
                SetZoom(1 / PixelScale, middle);
                break;
            case ZoomCommand.ZoomIn:
                SetZoom(_scale * KeyZoomStep, middle);
                break;
            case ZoomCommand.ZoomOut:
                SetZoom(_scale / KeyZoomStep, middle);
                break;
        }
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Fit()
    {
        _scale = FitScale;
        _center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        _isFitted = true;
        InvalidateVisual();
    }

    /// <summary>Zooms to <paramref name="scale"/> keeping the photo point under <paramref name="anchor"/> in place.</summary>
    private void SetZoom(double scale, Point anchor)
    {
        var clamped = Math.Clamp(scale, MinScale, MaxScale);
        var ratio = clamped / _scale;
        _center = anchor + (_center - anchor) * ratio;
        _scale = clamped;
        _isFitted = Math.Abs(_scale - FitScale) < 0.001 * FitScale;
        if (_isFitted)
        {
            _center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        }
        Constrain();
        if (_scale > FitScale * 1.01)
        {
            ZoomedBeyondFit?.Invoke(this, EventArgs.Empty);
        }
        InvalidateVisual();
    }

    /// <summary>Centers a dimension smaller than the viewport; otherwise keeps the viewport covered (no empty margin).</summary>
    private void Constrain()
    {
        var size = Shown * _scale;
        double Axis(double center, double extent, double viewport) => extent <= viewport
            ? viewport / 2
            : Math.Clamp(center, viewport - extent / 2, extent / 2);
        _center = new Point(Axis(_center.X, size.Width, Bounds.Width), Axis(_center.Y, size.Height, Bounds.Height));
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (Pager is not null)
        {
            Pager.Width = Bounds.Width;
        }
        if (_isFitted)
        {
            Fit();
        }
        else
        {
            _center += new Vector((e.NewSize.Width - e.PreviousSize.Width) / 2, (e.NewSize.Height - e.PreviousSize.Height) / 2);
            Constrain();
        }
    }

    // MARK: Drawing

    public override void Render(DrawingContext context)
    {
        // Avalonia hit-tests what is drawn: the whole canvas takes the pointer.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var offset = Pager?.Offset ?? 0;
        if (_picture is { } picture)
        {
            Draw(context, picture, _turns, _center + new Vector(offset, 0), _scale);
        }
        if (Pager?.Neighbor is { } neighbor && neighbor != 0 && Neighbor(neighbor) is { } next)
        {
            // The incoming photo, at its own fit, beside the current one.
            var size = FullSize(next, 0);
            var fit = size.Width <= 0 ? 1 : Math.Min(1 / PixelScale, Math.Min(Bounds.Width / size.Width, Bounds.Height / size.Height));
            var center = new Point(Bounds.Width / 2 + offset + neighbor * Bounds.Width, Bounds.Height / 2);
            Draw(context, next, 0, center, fit);
        }
    }

    private static void Draw(DrawingContext context, ViewerPicture picture, int turns, Point center, double scale)
    {
        var width = picture.FullWidth * scale;
        var height = picture.FullHeight * scale;
        var bitmap = picture.Bitmap;
        // Downscaled pictures want the smooth filter; past 100 % of the decoded pixels a cheaper one keeps panning light.
        var magnified = width > bitmap.PixelSize.Width || height > bitmap.PixelSize.Height;
        var matrix = Matrix.CreateRotation(turns * Math.PI / 2) * Matrix.CreateTranslation(center.X, center.Y);
        using (context.PushTransform(matrix))
        using (context.PushRenderOptions(new RenderOptions
        {
            BitmapInterpolationMode = magnified ? Avalonia.Media.Imaging.BitmapInterpolationMode.MediumQuality : Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality,
        }))
        {
            try
            {
                context.DrawImage(bitmap, new Rect(bitmap.Size), new Rect(-width / 2, -height / 2, width, height));
            }
            catch (ObjectDisposedException)
            {
                // A placeholder thumbnail the grid's cache let go of; the decoded picture replaces it shortly.
            }
        }
    }

    // MARK: Input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Activity?.Invoke(this, EventArgs.Empty);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }
        Focus();
        if (e.ClickCount == 2)
        {
            // Double-click: fit ↔ 100 % at the pointer.
            if (_isFitted)
            {
                SetZoom(1 / PixelScale, point.Position);
            }
            else
            {
                Fit();
            }
            ViewChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        _dragStart = point.Position;
        _dragLast = point.Position;
        _isSwiping = false;
        _isPanning = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Activity?.Invoke(this, EventArgs.Empty);
        if (_dragStart is not { } start)
        {
            return;
        }
        var position = e.GetPosition(this);
        var delta = position - _dragLast;
        _dragLast = position;
        if (!_isSwiping && !_isPanning)
        {
            // A few pixels of slack, so a click with a shaky hand stays a click.
            if (Math.Abs(position.X - start.X) < 4 && Math.Abs(position.Y - start.Y) < 4)
            {
                return;
            }
            if (_isFitted && Pager is not null)
            {
                _isSwiping = true;
                Pager.Begin();
                delta = position - start;
            }
            else
            {
                _isPanning = true;
            }
        }
        if (_isSwiping)
        {
            Pager!.Move(delta.X);
        }
        else
        {
            _center += delta;
            Constrain();
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        EndDrag(e.Pointer);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_isSwiping)
        {
            Pager?.End();
        }
        _dragStart = null;
        _isSwiping = false;
        _isPanning = false;
    }

    private void EndDrag(IPointer pointer)
    {
        if (_dragStart is null)
        {
            return;
        }
        if (_isSwiping)
        {
            Pager?.End();
        }
        _dragStart = null;
        _isSwiping = false;
        _isPanning = false;
        pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Activity?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
        var horizontal = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) || (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Delta.Y != 0);
        if (horizontal)
        {
            // Horizontal ticks (or Shift+wheel) turn pages: wheel right / down brings the next photo.
            var delta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
            _horizontalWheel += delta;
            if (Math.Abs(_horizontalWheel) >= 1 && Stopwatch.GetElapsedTime(_lastHorizontalPage) > HorizontalPageCooldown)
            {
                _lastHorizontalPage = Stopwatch.GetTimestamp();
                Navigate?.Invoke(this, _horizontalWheel > 0 ? -1 : 1);
                _horizontalWheel = 0;
            }
            return;
        }
        _horizontalWheel = 0;
        if (e.Delta.Y != 0)
        {
            // Vertical ticks zoom around the pointer.
            var steps = Math.Clamp(e.Delta.Y, -3, 3);
            SetZoom(_scale * Math.Pow(WheelZoomStep, steps), e.GetPosition(this));
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
