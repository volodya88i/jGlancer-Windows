using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JGlancer.Design;
using SkiaSharp;

namespace JGlancer.App.Support;

/// <summary>
/// Menus and panels that open in windows of their own (context menus, drop-downs, the key and tempo panel) become
/// frosted glass (W-111): what the app window shows behind them, blurred, under a tint of the theme's popup color. The
/// app draws it itself, so it does not depend on Windows' transparency effects, and it is a still picture taken when the
/// menu opens, which is all a menu needs. Off in the contrast themes and with Settings → Appearance → Frosted Glass off.
/// </summary>
internal static class PopupGlass
{
    /// <summary>The class of the popup look's border in the control themes (JGStyles: menus and panels).</summary>
    public const string PopupBorderClass = "popupSurface";

    public static void Install() =>
        Control.LoadedEvent.AddClassHandler<PopupRoot>((root, _) => Dispatcher.UIThread.Post(() => Apply(root), DispatcherPriority.Loaded));

    private static void Apply(PopupRoot root)
    {
        // A submenu's parent is the menu: the window under both is what shows through.
        var owner = root.ParentTopLevel;
        while (owner is PopupRoot parent)
        {
            owner = parent.ParentTopLevel;
        }
        if (!WindowGlass.IsOn || owner is null
            || !root.TryFindResource("JG.bg.popup", root.ActualThemeVariant, out var found) || found is not ISolidColorBrush popupBrush)
        {
            return;
        }
        var surfaces = root.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains(PopupBorderClass) && b.Bounds.Width > 0).ToList();
        if (surfaces.Count == 0)
        {
            return;
        }
        var scale = owner.RenderScaling;
        var size = new PixelSize((int)Math.Ceiling(owner.ClientSize.Width * scale), (int)Math.Ceiling(owner.ClientSize.Height * scale));
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }
        using var behind = TakePicture(owner, size, scale);
        var tint = popupBrush.Color;
        foreach (var surface in surfaces)
        {
            if (surface.TranslatePoint(default, root) is not { } inPopup)
            {
                continue;
            }
            var onScreen = root.PointToScreen(inPopup);
            var inOwner = owner.PointToClient(onScreen);
            var area = new Rect(inOwner, surface.Bounds.Size);
            surface.Background = new ImageBrush(Frost(behind, area, scale, tint)) { Stretch = Stretch.Fill };
        }
    }

    /// <summary>The window as it shows now, in device pixels.</summary>
    private static SKImage TakePicture(TopLevel owner, PixelSize size, double scale)
    {
        using var target = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        target.Render(owner);
        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            target.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, stride);
            return SKImage.FromPixelCopy(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul), pixels, stride);
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary><paramref name="area"/> of the window (DIPs), blurred, under the tint; the popup color where the window ends.</summary>
    private static WriteableBitmap Frost(SKImage behind, Rect area, double scale, Color tint)
    {
        var width = Math.Max(1, (int)Math.Ceiling(area.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(area.Height * scale));
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(tint.R, tint.G, tint.B));
        var sigma = (float)(JGMetrics.PopupGlassBlur * scale);
        using (var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp) })
        {
            canvas.DrawImage(behind, new SKPoint((float)(-area.X * scale), (float)(-area.Y * scale)), new SKSamplingOptions(SKFilterMode.Linear), blur);
        }
        using (var veil = new SKPaint { Color = new SKColor(tint.R, tint.G, tint.B, (byte)Math.Round(JGMetrics.PopupGlassTintOpacity * 255)) })
        {
            canvas.DrawRect(new SKRect(0, 0, width, height), veil);
        }
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96 * scale, 96 * scale), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var locked = bitmap.Lock())
        {
            surface.ReadPixels(info, locked.Address, locked.RowBytes, 0, 0);
        }
        return bitmap;
    }
}
