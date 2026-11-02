using System.Globalization;
using System.Runtime.Versioning;
using JGlancer.Platform.Windows.Com;

namespace JGlancer.Platform.Windows;

/// <summary>
/// Photo facts through WIC without decoding pixels: frame size, EXIF orientation and capture date (with its offset
/// when the file has one). Works for every format with a WIC codec: JPEG, PNG, TIFF, GIF, BMP, WebP, and HEIC / RAW
/// / AVIF when the Store extensions are installed.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe class WicPhotoReader
{
    private const uint GenericRead = 0x80000000;
    private const int CacheOnDemand = 0;
    private static readonly Guid FactoryClass = new("cacaf262-9370-4615-a13b-9f5539da4c0a");

    // JPEG keeps EXIF in APP1; TIFF, RAW and HEIF expose the IFD at the root.
    private static readonly string[] OrientationPaths = ["/app1/ifd/{ushort=274}", "/ifd/{ushort=274}"];
    private static readonly string[] DatePaths =
    [
        "/app1/ifd/exif/{ushort=36867}", "/ifd/exif/{ushort=36867}", // DateTimeOriginal
        "/app1/ifd/exif/{ushort=36868}", "/ifd/exif/{ushort=36868}", // DateTimeDigitized
        "/app1/ifd/{ushort=306}", "/ifd/{ushort=306}", // TIFF DateTime
    ];
    private static readonly string[] OffsetPaths = ["/app1/ifd/exif/{ushort=36881}", "/ifd/exif/{ushort=36881}"];

    [ThreadStatic]
    private static IWICImagingFactory? t_factory;

    /// <summary>The facts read from a photo.</summary>
    public sealed record Facts(int PixelWidth, int PixelHeight, int Orientation, string? DateText, string? OffsetText, DateTimeOffset? PolicyDate);

    /// <summary>Reads <paramref name="path"/>; null when WIC cannot open it.</summary>
    public static Facts? Read(string path)
    {
        var factory = t_factory ??= ComInterop.Create<IWICImagingFactory>(FactoryClass, typeof(IWICImagingFactory).GUID);
        IWICBitmapDecoder? decoder = null;
        IWICBitmapFrameDecode? frame = null;
        IWICMetadataQueryReader? metadata = null;
        try
        {
            nint pointer;
            if (factory.CreateDecoderFromFilename(path, null, GenericRead, CacheOnDemand, &pointer) < 0 || pointer == 0)
            {
                return null;
            }
            decoder = ComInterop.Wrap<IWICBitmapDecoder>(pointer);
            if (decoder.GetFrame(0, &pointer) < 0 || pointer == 0)
            {
                return null;
            }
            frame = ComInterop.Wrap<IWICBitmapFrameDecode>(pointer);
            uint width;
            uint height;
            if (frame.GetSize(&width, &height) < 0 || width == 0 || height == 0)
            {
                return null;
            }
            // Formats without metadata (BMP, some GIFs) fail here; size alone is still a result.
            if (frame.GetMetadataQueryReader(&pointer) >= 0 && pointer != 0)
            {
                metadata = ComInterop.Wrap<IWICMetadataQueryReader>(pointer);
            }
            var orientation = (int)(Integer(metadata, [.. OrientationPaths, "System.Photo.Orientation"]) ?? 1);
            return new Facts(
                (int)width,
                (int)height,
                orientation is >= 1 and <= 8 ? orientation : 1,
                Text(metadata, DatePaths),
                Text(metadata, OffsetPaths),
                Date(metadata, "System.Photo.DateTaken"));
        }
        finally
        {
            ComInterop.Release(metadata);
            ComInterop.Release(frame);
            ComInterop.Release(decoder);
        }
    }

    /// <summary>Diagnostics for <see cref="MetadataDump"/>.</summary>
    public static string Describe(string path)
    {
        var facts = Read(path);
        return facts is null
            ? "  WIC: cannot open\n"
            : string.Create(CultureInfo.InvariantCulture,
                $"  WIC: {facts.PixelWidth}x{facts.PixelHeight} orientation {facts.Orientation} date '{facts.DateText}' offset '{facts.OffsetText}' policy {facts.PolicyDate:O}\n");
    }

    /// <summary>The EXIF orientation (1–8) of an open frame; 1 when it has none.</summary>
    internal static int Orientation(IWICBitmapFrameDecode frame)
    {
        nint pointer;
        if (frame.GetMetadataQueryReader(&pointer) < 0 || pointer == 0)
        {
            return 1;
        }
        var metadata = ComInterop.Wrap<IWICMetadataQueryReader>(pointer);
        try
        {
            var orientation = (int)(Integer(metadata, [.. OrientationPaths, "System.Photo.Orientation"]) ?? 1);
            return orientation is >= 1 and <= 8 ? orientation : 1;
        }
        finally
        {
            ComInterop.Release(metadata);
        }
    }

    /// <summary>The first query path that yields text (EXIF ASCII values arrive as ANSI strings).</summary>
    private static string? Text(IWICMetadataQueryReader? metadata, string[] names)
    {
        foreach (var name in names)
        {
            var value = default(PropVariant);
            try
            {
                if (metadata is not null && metadata.GetMetadataByName(name, &value) >= 0 && value.Text is { Length: > 0 } text)
                {
                    return text.TrimEnd('\0', ' ');
                }
            }
            finally
            {
                value.Clear();
            }
        }
        return null;
    }

    /// <summary>The first query path that yields an integer.</summary>
    private static long? Integer(IWICMetadataQueryReader? metadata, string[] names)
    {
        foreach (var name in names)
        {
            var value = default(PropVariant);
            try
            {
                if (metadata is not null && metadata.GetMetadataByName(name, &value) >= 0 && value.Integer is { } number)
                {
                    return number;
                }
            }
            finally
            {
                value.Clear();
            }
        }
        return null;
    }

    private static DateTimeOffset? Date(IWICMetadataQueryReader? metadata, string name)
    {
        var value = default(PropVariant);
        try
        {
            return metadata is not null && metadata.GetMetadataByName(name, &value) >= 0 ? value.Date : null;
        }
        finally
        {
            value.Clear();
        }
    }
}
