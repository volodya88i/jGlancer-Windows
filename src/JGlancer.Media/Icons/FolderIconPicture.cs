using System.Runtime.InteropServices;
using JGlancer.Core.Model;
using JGlancer.Media.Thumbnails;
using SkiaSharp;

namespace JGlancer.Media.Icons;

/// <summary>
/// Square pictures for library folder icons (macOS D-129): the middle of a photo, <see cref="FolderIcon.PictureSide"/>
/// pixels, as JPEG at 85 % (sRGB, as everything the app draws, W-113).
/// </summary>
public static class FolderIconPicture
{
    public const int JpegQuality = 85;

    /// <summary>The picture from a decoded photo or thumbnail (BGRA premultiplied); null when it cannot be made.</summary>
    public static byte[]? Make(ThumbnailImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var side = Math.Min(image.Width, image.Height);
        if (side <= 0)
        {
            return null;
        }
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var handle = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);
        try
        {
            using var source = new SKPixmap(info, handle.AddrOfPinnedObject(), image.Stride);
            using var whole = SKImage.FromPixels(source);
            var target = FolderIcon.PictureSide;
            using var surface = SKSurface.Create(new SKImageInfo(target, target, SKColorType.Bgra8888, SKAlphaType.Premul));
            if (surface is null)
            {
                return null;
            }
            var crop = SKRect.Create((image.Width - side) / 2f, (image.Height - side) / 2f, side, side);
            // A picture with transparency lies on black, as JPEG keeps no alpha.
            surface.Canvas.Clear(SKColors.Black);
            using var paint = new SKPaint();
            surface.Canvas.DrawImage(whole, crop, SKRect.Create(target, target), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
            using var picture = surface.Snapshot();
            using var data = picture.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
            return data?.ToArray();
        }
        finally
        {
            handle.Free();
        }
    }
}
