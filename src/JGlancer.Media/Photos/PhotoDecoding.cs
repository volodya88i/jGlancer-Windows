using JGlancer.Media.Thumbnails;

namespace JGlancer.Media.Photos;

/// <summary>
/// A photo decoded for display: pixels (BGRA, premultiplied, orientation applied) and the photo's full pixel size
/// after orientation, so the viewer knows what 100 % is while it shows a screen-size image.
/// </summary>
public sealed record DecodedPhoto(ThumbnailImage Image, int FullWidth, int FullHeight)
{
    /// <summary>True when the pixels are the photo at full resolution.</summary>
    public bool IsFullResolution => Image.Width >= FullWidth && Image.Height >= FullHeight;
}

/// <summary>Scale factors for decoding at size.</summary>
public static class PhotoScale
{
    /// <summary>
    /// The scale (at most 1) at which a <paramref name="width"/> × <paramref name="height"/> photo fits the box in the
    /// better of its two orientations.
    /// </summary>
    public static double ToFit(double width, double height, double boxWidth, double boxHeight)
    {
        if (width <= 0 || height <= 0)
        {
            return 1;
        }
        var upright = Math.Min(boxWidth / width, boxHeight / height);
        var turned = Math.Min(boxWidth / height, boxHeight / width);
        return Math.Min(1, Math.Max(upright, turned));
    }
}

/// <summary>Where a decode writes its pixels: rows of premultiplied BGRA, <paramref name="Stride"/> bytes apart.</summary>
public readonly record struct PixelTarget(nint Address, int Stride);

/// <summary>Decodes photos for the viewer (macOS PhotoDecoder); platform layer, fakes in tests.</summary>
public interface IPhotoDecoder
{
    /// <summary>A box that asks for full resolution.</summary>
    public const int Unbounded = int.MaxValue;

    /// <summary>
    /// The photo just large enough to fit a <paramref name="boxWidth"/> × <paramref name="boxHeight"/> box (the
    /// screen) in either orientation, so a view rotation still fits sharp; never upscaled; decoded at size, not decoded
    /// then resized. Null when it cannot be read. Blocking; call it on a worker thread.
    /// </summary>
    DecodedPhoto? Decode(string path, int boxWidth, int boxHeight, CancellationToken cancellation);

    /// <summary>
    /// As <see cref="Decode"/>, but into memory the caller provides (a bitmap's own pixels: no managed buffer, no
    /// copy, W12). <paramref name="target"/> gets the oriented width and height and returns where to write, or null to
    /// give up. Returns the photo's full size after orientation; null when it cannot be read. Blocking.
    /// </summary>
    (int FullWidth, int FullHeight)? DecodeInto(
        string path, int boxWidth, int boxHeight, Func<int, int, PixelTarget?> target, CancellationToken cancellation);
}
