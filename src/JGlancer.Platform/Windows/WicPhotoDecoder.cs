using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JGlancer.Media.Photos;
using JGlancer.Media.Thumbnails;
using JGlancer.Platform.Windows.Com;

namespace JGlancer.Platform.Windows;

/// <summary>
/// Photos through WIC (macOS PhotoDecoder): the first frame decoded at size by the scaler (JPEG decodes at 1/2, 1/4,
/// 1/8 resolution directly), EXIF orientation applied by the flip-rotator, converted to premultiplied BGRA. Used by the
/// viewer (screen size, full resolution on zoom) and by the thumbnail generator.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class WicPhotoDecoder : IPhotoDecoder
{
    private const uint GenericRead = 0x80000000;
    private const int CacheOnDemand = 0;
    private const int InterpolationFant = 3;

    /// <summary>The scaler's interpolation (experiments from the tools).</summary>
    internal static int Interpolation { get; set; } = InterpolationFant;
    private static readonly Guid FactoryClass = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    private static readonly Guid PixelFormatPbgra = new("6fddc324-4e03-4bfe-b185-3d77768dc910");

    // WIC objects are free-threaded, but one factory per thread keeps COM apartments out of the picture.
    [ThreadStatic]
    private static IWICImagingFactory? t_factory;

    public DecodedPhoto? Decode(string path, int boxWidth, int boxHeight, CancellationToken cancellation)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            return cancellation.IsCancellationRequested ? null : DecodeFile(path, boxWidth, boxHeight);
        }
        catch (Exception error) when (error is COMException or InvalidCastException or IOException or OutOfMemoryException)
        {
            return null;
        }
    }

    public (int FullWidth, int FullHeight)? DecodeInto(
        string path, int boxWidth, int boxHeight, Func<int, int, PixelTarget?> target, CancellationToken cancellation)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(target);
        try
        {
            return cancellation.IsCancellationRequested ? null : Run(path, boxWidth, boxHeight, (converter, width, height, orientation, full) =>
                CopyInto(converter, width, height, orientation, target) ? full : ((int, int)?)null);
        }
        catch (Exception error) when (error is COMException or InvalidCastException or IOException or OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>The converted pixels into the caller's memory, turned as the EXIF orientation says.</summary>
    private static bool CopyInto(IWICFormatConverter converter, uint width, uint height, int orientation, Func<int, int, PixelTarget?> target)
    {
        var turned = orientation is >= 5 and <= 8;
        if (target(turned ? (int)height : (int)width, turned ? (int)width : (int)height) is not { } into)
        {
            return false;
        }
        if (orientation is < 2 or > 8)
        {
            // Upright: straight into the target.
            return converter.CopyPixels(0, (uint)into.Stride, checked((uint)(into.Stride * (int)height)), (byte*)into.Address) >= 0;
        }
        // Turned or mirrored: through one native buffer (never the managed heap), turned into the target.
        var size = checked((nuint)width * height * 4);
        var buffer = (byte*)NativeMemory.Alloc(size);
        try
        {
            if (converter.CopyPixels(0, width * 4, (uint)size, buffer) < 0)
            {
                return false;
            }
            OrientInto(new ReadOnlySpan<uint>(buffer, checked((int)(width * height))), (int)width, (int)height, orientation, into);
            return true;
        }
        finally
        {
            NativeMemory.Free(buffer);
        }
    }

    /// <summary>The decode itself; null when WIC has no codec for the file or it is broken. May throw COM errors.</summary>
    internal static DecodedPhoto? DecodeFile(string path, int boxWidth, int boxHeight) =>
        Run(path, boxWidth, boxHeight, (converter, width, height, orientation, full) =>
        {
            var pixels = GC.AllocateUninitializedArray<byte>(checked((int)(width * height * 4)));
            fixed (byte* buffer = pixels)
            {
                if (converter.CopyPixels(0, width * 4, (uint)pixels.Length, buffer) < 0)
                {
                    return null;
                }
            }
            // Orientation is applied to the decoded pixels, not by a WIC flip-rotator: over a scaler the rotator
            // re-reads the source again and again (12 s for one portrait phone photo, W-37).
            var (oriented, orientedWidth, orientedHeight) = Orient(pixels, (int)width, (int)height, orientation);
            return new DecodedPhoto(new ThumbnailImage(orientedWidth, orientedHeight, oriented), full.FullWidth, full.FullHeight);
        });

    /// <summary>
    /// Opens the first frame, scales it to fit the box and converts it to premultiplied BGRA, then hands the converter,
    /// its size, the EXIF orientation and the full oriented size to <paramref name="consume"/>; the COM objects live
    /// until it returns.
    /// </summary>
    private static T? Run<T>(string path, int boxWidth, int boxHeight,
        Func<IWICFormatConverter, uint, uint, int, (int FullWidth, int FullHeight), T?> consume)
    {
        var factory = t_factory ??= ComInterop.Create<IWICImagingFactory>(FactoryClass, typeof(IWICImagingFactory).GUID);
        var owned = new List<object>();
        try
        {
            nint pointer;
            if (factory.CreateDecoderFromFilename(Win32.LongPath(path), null, GenericRead, CacheOnDemand, &pointer) < 0 || pointer == 0)
            {
                return default;
            }
            var decoder = Own<IWICBitmapDecoder>(pointer, owned);
            if (decoder.GetFrame(0, &pointer) < 0 || pointer == 0)
            {
                return default;
            }
            var frame = Own<IWICBitmapFrameDecode>(pointer, owned);
            uint width;
            uint height;
            if (frame.GetSize(&width, &height) < 0 || width == 0 || height == 0)
            {
                return default;
            }
            var orientation = WicPhotoReader.Orientation(frame);
            var (fullWidth, fullHeight) = orientation >= 5 ? ((int)height, (int)width) : ((int)width, (int)height);
            IWICBitmapSource source = frame;
            var scale = PhotoScale.ToFit(width, height, boxWidth, boxHeight);
            if (scale < 1)
            {
                var scaled = (Math.Max(1, (uint)Math.Round(width * scale)), Math.Max(1, (uint)Math.Round(height * scale)));
                Check(factory.CreateBitmapScaler(&pointer));
                var scaler = Own<IWICBitmapScaler>(pointer, owned);
                Check(scaler.Initialize(source, scaled.Item1, scaled.Item2, Interpolation));
                source = scaler;
                (width, height) = scaled;
            }
            Check(factory.CreateFormatConverter(&pointer));
            var converter = Own<IWICFormatConverter>(pointer, owned);
            var format = PixelFormatPbgra;
            Check(converter.Initialize(source, &format, 0, 0, 0, 0));
            return consume(converter, width, height, orientation, (fullWidth, fullHeight));
        }
        finally
        {
            // Released newest first: each stage holds the one before it.
            for (var i = owned.Count - 1; i >= 0; i--)
            {
                ComInterop.Release(owned[i]);
            }
        }
    }

    /// <summary>
    /// 32-bit pixels of a <paramref name="width"/> × <paramref name="height"/> image turned as EXIF
    /// <paramref name="orientation"/> says: 2 mirror, 3 half turn, 4 flip, 5 transpose, 6 quarter turn clockwise,
    /// 7 transverse, 8 quarter turn counterclockwise. 1 and unknown values return the pixels as they are.
    /// </summary>
    internal static (byte[] Pixels, int Width, int Height) Orient(byte[] pixels, int width, int height, int orientation)
    {
        if (orientation is < 2 or > 8)
        {
            return (pixels, width, height);
        }
        var turned = orientation >= 5;
        var (outWidth, outHeight) = turned ? (height, width) : (width, height);
        var result = GC.AllocateUninitializedArray<byte>(pixels.Length);
        var source = MemoryMarshal.Cast<byte, uint>(pixels);
        var target = MemoryMarshal.Cast<byte, uint>(result.AsSpan());
        for (var y = 0; y < outHeight; y++)
        {
            for (var x = 0; x < outWidth; x++)
            {
                var (sx, sy) = orientation switch
                {
                    2 => (width - 1 - x, y),
                    3 => (width - 1 - x, height - 1 - y),
                    4 => (x, height - 1 - y),
                    5 => (y, x),
                    6 => (y, height - 1 - x),
                    7 => (width - 1 - y, height - 1 - x),
                    _ => (width - 1 - y, x),
                };
                target[y * outWidth + x] = source[sy * width + sx];
            }
        }
        return (result, outWidth, outHeight);
    }
    /// <summary><see cref="Orient"/> from native pixels into a target with its own row stride.</summary>
    private static void OrientInto(ReadOnlySpan<uint> source, int width, int height, int orientation, PixelTarget into)
    {
        var turned = orientation >= 5;
        var (outWidth, outHeight) = turned ? (height, width) : (width, height);
        for (var y = 0; y < outHeight; y++)
        {
            var row = new Span<uint>((byte*)into.Address + (nint)y * into.Stride, outWidth);
            for (var x = 0; x < outWidth; x++)
            {
                var (sx, sy) = orientation switch
                {
                    2 => (width - 1 - x, y),
                    3 => (width - 1 - x, height - 1 - y),
                    4 => (x, height - 1 - y),
                    5 => (y, x),
                    6 => (y, height - 1 - x),
                    7 => (width - 1 - y, height - 1 - x),
                    _ => (width - 1 - y, x),
                };
                row[x] = source[sy * width + sx];
            }
        }
    }

    private static T Own<T>(nint pointer, List<object> owned)
        where T : class
    {
        var wrapped = ComInterop.Wrap<T>(pointer);
        owned.Add(wrapped);
        return wrapped;
    }

    private static void Check(int result) => Marshal.ThrowExceptionForHR(result);
}
