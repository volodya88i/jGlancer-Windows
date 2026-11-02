using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace JGlancer.App.Support;

/// <summary>
/// A picture like Avalonia's <see cref="Image"/> (same stretching and drawing), except that a bitmap disposed while
/// still shown draws nothing instead of crashing the app (W-91: covers and pictures are disposed as soon as the next
/// one arrives; a view that missed the change kept a dead bitmap and the next render threw ObjectDisposedException).
/// </summary>
internal sealed class SafeImage : Control
{
    public static readonly StyledProperty<IImage?> SourceProperty = AvaloniaProperty.Register<SafeImage, IImage?>(nameof(Source));

    public static readonly StyledProperty<Stretch> StretchProperty = AvaloniaProperty.Register<SafeImage, Stretch>(nameof(Stretch), Stretch.Uniform);

    static SafeImage()
    {
        AffectsRender<SafeImage>(SourceProperty, StretchProperty);
        AffectsMeasure<SafeImage>(SourceProperty, StretchProperty);
    }

    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    /// <summary>The picture's size, or null when there is none or it was disposed.</summary>
    private Size? LiveSize
    {
        get
        {
            if (Source is not { } source)
            {
                return null;
            }
            try
            {
                return source.Size;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        if (Source is not { } source || LiveSize is not { Width: > 0, Height: > 0 } sourceSize || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }
        var viewPort = new Rect(Bounds.Size);
        var scale = Stretch.CalculateScaling(Bounds.Size, sourceSize);
        var scaledSize = sourceSize * scale;
        var destination = viewPort.CenterRect(new Rect(scaledSize)).Intersect(viewPort);
        var sourceRect = new Rect(sourceSize).CenterRect(new Rect(destination.Size / scale));
        try
        {
            context.DrawImage(source, sourceRect, destination);
        }
        catch (ObjectDisposedException)
        {
            // Disposed between the check and the draw: nothing this frame.
        }
    }

    protected override Size MeasureOverride(Size availableSize) =>
        LiveSize is { } size ? Stretch.CalculateSize(availableSize, size) : default;

    protected override Size ArrangeOverride(Size finalSize) =>
        LiveSize is { } size ? Stretch.CalculateSize(finalSize, size) : default;
}
