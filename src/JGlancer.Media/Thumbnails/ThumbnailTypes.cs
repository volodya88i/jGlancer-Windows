using JGlancer.Core.Model;

namespace JGlancer.Media.Thumbnails;

/// <summary>Thumbnail size buckets (longest side). Values are persisted in <c>cache.sqlite</c> and must never change.</summary>
public enum ThumbnailBucket
{
    /// <summary>160 px.</summary>
    Small = 0,

    /// <summary>320 px.</summary>
    Medium = 1,

    /// <summary>640 px.</summary>
    Large = 2,
}

public static class ThumbnailBuckets
{
    /// <summary>The longest side of a thumbnail in <paramref name="bucket"/>, in pixels.</summary>
    public static int MaxPixelSize(this ThumbnailBucket bucket) => bucket switch
    {
        ThumbnailBucket.Small => 160,
        ThumbnailBucket.Medium => 320,
        _ => 640,
    };

    /// <summary>The smallest bucket covering <paramref name="pixels"/> on the longest side, or Large beyond every bucket.</summary>
    public static ThumbnailBucket Covering(double pixels) =>
        Enum.GetValues<ThumbnailBucket>().FirstOrDefault(b => b.MaxPixelSize() >= pixels, ThumbnailBucket.Large);
}

/// <summary>Scheduling priority: visible tiles first, then the prefetch window, then background work.</summary>
public enum ThumbnailPriority
{
    Background = 0,
    Nearby = 1,
    Visible = 2,
}

/// <summary>Identity of a stored thumbnail: asset, file modification time (ms) and bucket.</summary>
public readonly record struct ThumbnailKey(long AssetId, long ModifiedAt, ThumbnailBucket Bucket);

/// <summary>What to make a thumbnail of.</summary>
/// <param name="ModifiedAt">Part of the cache key: a modified file gets a new thumbnail automatically.</param>
/// <param name="Duration">Seconds, for choosing a video poster frame; null if unknown.</param>
/// <param name="Bucket">The bucket the display needs.</param>
public sealed record ThumbnailRequest(
    long AssetId, string Path, MediaKind Kind, string? TypeIdentifier, DateTimeOffset ModifiedAt, double? Duration, ThumbnailBucket Bucket)
{
    /// <summary>Stored buckets are medium and large; small requests reuse medium, scaled down at display.</summary>
    public ThumbnailBucket StoredBucket => Bucket < ThumbnailBucket.Medium ? ThumbnailBucket.Medium : Bucket;

    public ThumbnailKey Key => new(AssetId, UnixMilliseconds.From(ModifiedAt), StoredBucket);

    public static ThumbnailRequest For(Asset asset, ThumbnailBucket bucket)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return new ThumbnailRequest(asset.Id, asset.Path, asset.Kind, asset.TypeIdentifier, asset.ModifiedAt, asset.Duration, bucket);
    }
}

/// <summary>
/// A decoded thumbnail: 32-bit BGRA, premultiplied alpha, rows packed (<c>stride = width × 4</c>), the layout Skia and
/// Avalonia draw without conversion.
/// </summary>
public sealed class ThumbnailImage
{
    public ThumbnailImage(int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0 || pixels.Length != width * height * 4)
        {
            throw new ArgumentException("pixel buffer does not match the size", nameof(pixels));
        }
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }
    public int Stride => Width * 4;

    /// <summary>Whether any pixel is not fully opaque.</summary>
    public bool HasAlpha
    {
        get
        {
            for (var i = 3; i < Pixels.Length; i += 4)
            {
                if (Pixels[i] != 255)
                {
                    return true;
                }
            }
            return false;
        }
    }
}

/// <summary>Produces a thumbnail from the original file (platform layer; fakes in tests).</summary>
public interface IThumbnailGenerator
{
    /// <summary>
    /// A decoded image whose longest side is at most <paramref name="maxPixelSize"/> (never upscaled), orientation
    /// applied, or null if the file has none. Blocking; the service calls it on a worker thread.
    /// </summary>
    ThumbnailImage? Generate(ThumbnailRequest request, int maxPixelSize, CancellationToken cancellation);
}
