using System.Runtime.InteropServices;
using SkiaSharp;

namespace JGlancer.Media.Thumbnails;

/// <summary>
/// Encoding of thumbnails for <c>cache.sqlite</c>: JPEG at quality 80, or PNG when transparency must survive (macOS
/// D-36). Skia, which Avalonia already ships, does the work.
/// </summary>
public static class ThumbnailCodec
{
    public const int JpegQuality = 80;

    /// <summary>Source types whose transparency is kept (graphics, screenshots); photos and video frames are opaque.</summary>
    private static readonly HashSet<string> TransparentTypes = new(StringComparer.Ordinal)
    {
        "public.png", "com.compuserve.gif", "org.webmproject.webp", "public.tiff", "public.avif", "com.adobe.photoshop-image",
    };

    public static bool PreservesAlpha(string? typeIdentifier) => typeIdentifier is not null && TransparentTypes.Contains(typeIdentifier);

    public static byte[] Encode(ThumbnailImage image, bool preserveAlpha)
    {
        ArgumentNullException.ThrowIfNull(image);
        var png = preserveAlpha && image.HasAlpha;
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var handle = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);
        try
        {
            using var pixmap = new SKPixmap(info, handle.AddrOfPinnedObject(), image.Stride);
            using var data = png
                ? pixmap.Encode(SKPngEncoderOptions.Default)
                : pixmap.Encode(new SKJpegEncoderOptions(JpegQuality, SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore));
            return data?.ToArray() ?? [];
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>Decodes a stored thumbnail into BGRA premultiplied pixels; null for unreadable data.</summary>
    public static ThumbnailImage? Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var stream = new SKMemoryStream(data);
        using var codec = SKCodec.Create(stream);
        if (codec is null || codec.Info.Width == 0 || codec.Info.Height == 0)
        {
            return null;
        }
        // Decoded straight into the display layout, no intermediate bitmap.
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var pixels = new byte[info.BytesSize];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            var result = codec.GetPixels(info, handle.AddrOfPinnedObject());
            return result is SKCodecResult.Success or SKCodecResult.IncompleteInput
                ? new ThumbnailImage(info.Width, info.Height, pixels)
                : null;
        }
        finally
        {
            handle.Free();
        }
    }
}
