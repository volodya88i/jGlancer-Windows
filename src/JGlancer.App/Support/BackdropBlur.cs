using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace JGlancer.App.Support;

/// <summary>
/// A frosted backdrop for the viewers' strips and controls: what the window
/// drew behind this control in the frame so far (photo, video, cover backdrop) is copied on the GPU and drawn back
/// blurred, under the scrim. Nothing when the renderer gives no Skia surface (offscreen snapshots): the scrim alone
/// shows, as before.
/// </summary>
internal sealed class BackdropBlur : Control
{
    public static readonly StyledProperty<double> RadiusProperty =
        AvaloniaProperty.Register<BackdropBlur, double>(nameof(Radius), Design.JGMetrics.BackdropBlurRadius);

    static BackdropBlur()
    {
        AffectsRender<BackdropBlur>(RadiusProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<BackdropBlur>(false);
    }

    /// <summary>The blur's standard deviation in effective pixels.</summary>
    public double Radius
    {
        get => GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Bounds.Width > 0 && Bounds.Height > 0 && Radius > 0)
        {
            context.Custom(new Operation(new Rect(Bounds.Size), Radius));
        }
    }

    private sealed class Operation(Rect bounds, double radius) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point point) => false;

        // Always drawn again: what lies behind changes without this control knowing.
        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            using var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()?.Lease();
            if (lease?.SkSurface is not { } surface)
            {
                return;
            }
            var canvas = lease.SkCanvas;
            var device = canvas.TotalMatrix.MapRect(new SKRect((float)bounds.X, (float)bounds.Y, (float)bounds.Right, (float)bounds.Bottom));
            // Only what is on the surface and inside the area being drawn now.
            var area = SKRectI.Intersect(SKRectI.Round(device), canvas.DeviceClipBounds);
            if (area.Width <= 0 || area.Height <= 0)
            {
                return;
            }
            using var behind = surface.Snapshot(area);
            if (behind is null)
            {
                return;
            }
            var sigma = (float)(radius * canvas.TotalMatrix.ScaleX);
            using var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp) };
            canvas.Save();
            canvas.ResetMatrix();
            canvas.ClipRect(device);
            canvas.DrawImage(behind, area.Left, area.Top, paint);
            canvas.Restore();
        }
    }
}
