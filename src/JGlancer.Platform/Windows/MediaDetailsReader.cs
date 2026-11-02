using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JGlancer.Core.Model;
using JGlancer.Platform.Windows.Com;

namespace JGlancer.Platform.Windows;

/// <summary>
/// The extended facts of a file (macOS MediaDetails.read) from the Shell property system: EXIF camera, exposure and
/// GPS of photos (no pixels decoded), codecs, rates and channels of video and audio (Media Foundation handlers); the
/// color profile's name from the photo's embedded ICC profile through WIC. Formats Windows has no handler for (AIFF,
/// Matroska, Ogg) give nothing here: the app asks libmpv for those.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class MediaDetailsReader
{
    private static readonly PropertyKey? CameraMake = Key("System.Photo.CameraManufacturer");
    private static readonly PropertyKey? CameraModel = Key("System.Photo.CameraModel");
    private static readonly PropertyKey? LensModel = Key("System.Photo.LensModel");
    private static readonly PropertyKey? FocalLength = Key("System.Photo.FocalLength");
    private static readonly PropertyKey? FNumber = Key("System.Photo.FNumber");
    private static readonly PropertyKey? ExposureTime = Key("System.Photo.ExposureTime");
    private static readonly PropertyKey? IsoSpeed = Key("System.Photo.ISOSpeed");
    private static readonly PropertyKey? Latitude = Key("System.GPS.Latitude");
    private static readonly PropertyKey? LatitudeRef = Key("System.GPS.LatitudeRef");
    private static readonly PropertyKey? Longitude = Key("System.GPS.Longitude");
    private static readonly PropertyKey? LongitudeRef = Key("System.GPS.LongitudeRef");
    private static readonly PropertyKey? Altitude = Key("System.GPS.Altitude");
    private static readonly PropertyKey? AltitudeRef = Key("System.GPS.AltitudeRef");
    private static readonly PropertyKey? ImageBitDepth = Key("System.Image.BitDepth");
    private static readonly PropertyKey? ImageColorSpace = Key("System.Image.ColorSpace");
    private static readonly PropertyKey? VideoCompression = Key("System.Video.Compression");
    private static readonly PropertyKey? FrameRate = Key("System.Video.FrameRate");
    private static readonly PropertyKey? VideoBitrate = Key("System.Video.EncodingBitrate");
    private static readonly PropertyKey? AudioFormat = Key("System.Audio.Format");
    private static readonly PropertyKey? AudioBitrate = Key("System.Audio.EncodingBitrate");
    private static readonly PropertyKey? SampleRate = Key("System.Audio.SampleRate");
    private static readonly PropertyKey? SampleSize = Key("System.Audio.SampleSize");
    private static readonly PropertyKey? ChannelCount = Key("System.Audio.ChannelCount");

    private const uint GenericRead = 0x80000000;
    private const int CacheOnDemand = 0;
    private static readonly Guid FactoryClass = new("cacaf262-9370-4615-a13b-9f5539da4c0a");

    /// <summary>Reads <paramref name="path"/> (blocking: call it off the UI thread); empty when nothing is known.</summary>
    public static MediaDetails Read(string path, MediaKind kind)
    {
        var store = ComInterop.PropertyStore(path);
        if (store is null)
        {
            return MediaDetails.Empty;
        }
        try
        {
            return kind == MediaKind.Photo ? Photo(store, path) : Audiovisual(store);
        }
        finally
        {
            ComInterop.Release(store);
        }
    }

    private static MediaDetails Photo(IPropertyStore store, string path)
    {
        var below = Value(store, AltitudeRef, v => v.Integer) == 1;
        return new MediaDetails
        {
            Camera = MediaDetails.CameraName(Value(store, CameraMake, v => v.Text), Value(store, CameraModel, v => v.Text)),
            Lens = Value(store, LensModel, v => v.Text)?.Trim() is { Length: > 0 } lens ? lens : null,
            FocalLength = Positive(Value(store, FocalLength, v => v.Number)),
            Aperture = Positive(Value(store, FNumber, v => v.Number)),
            ExposureTime = Positive(Value(store, ExposureTime, v => v.Number)),
            Iso = Value(store, IsoSpeed, v => v.Integer) is > 0 and var iso ? (int)iso : null,
            Latitude = Degrees(Value(store, Latitude, v => v.Doubles), Value(store, LatitudeRef, v => v.Text), "S"),
            Longitude = Degrees(Value(store, Longitude, v => v.Doubles), Value(store, LongitudeRef, v => v.Text), "W"),
            Altitude = Value(store, Altitude, v => v.Number) is { } meters ? (below ? -meters : meters) : null,
            ColorProfile = ProfileName(path) ?? (Value(store, ImageColorSpace, v => v.Integer) == 1 ? "sRGB" : null),
            BitDepth = Value(store, ImageBitDepth, v => v.Integer) is > 0 and var depth ? PerChannel((int)depth) : null,
        };
    }

    private static MediaDetails Audiovisual(IPropertyStore store)
    {
        var video = ShellMediaReader.Codecs.Video(Value(store, VideoCompression, v => v.Text));
        var audio = ShellMediaReader.Codecs.Audio(Value(store, AudioFormat, v => v.Text));
        return new MediaDetails
        {
            VideoCodec = video is null ? null : MediaDetails.CodecName(video),
            FrameRate = Value(store, FrameRate, v => v.Integer) is > 0 and var rate ? rate / 1000d : null,
            VideoBitrate = video is null ? null : Positive(Value(store, VideoBitrate, v => v.Number)),
            AudioCodec = audio is null ? null : MediaDetails.CodecName(audio),
            AudioBitrate = audio is null ? null : Positive(Value(store, AudioBitrate, v => v.Number)),
            SampleRate = audio is null ? null : Positive(Value(store, SampleRate, v => v.Number)),
            Channels = audio is not null && Value(store, ChannelCount, v => v.Integer) is > 0 and var channels ? (int)channels : null,
            // Lossy codecs report a nominal sample size; only PCM-like ones have a meaningful depth.
            BitDepth = audio is "lpcm" or "flac" or "alac" && Value(store, SampleSize, v => v.Integer) is > 0 and var depth ? (int)depth : null,
        };
    }

    /// <summary>
    /// Bits per channel from the Shell's bits per pixel: 24 and 48 are three channels (RGB), 32 and 64 four (with alpha
    /// or CMYK); 8 and 16 are one (gray), 1 and 4 palettes as they are.
    /// </summary>
    internal static int PerChannel(int bitsPerPixel) => bitsPerPixel switch
    {
        24 or 48 => bitsPerPixel / 3,
        32 or 64 => bitsPerPixel / 4,
        _ => bitsPerPixel,
    };

    /// <summary>EXIF GPS degrees, minutes, seconds → signed decimal degrees.</summary>
    private static double? Degrees(double[]? parts, string? reference, string negative)
    {
        if (parts is not { Length: > 0 })
        {
            return null;
        }
        var degrees = parts[0] + (parts.Length > 1 ? parts[1] / 60 : 0) + (parts.Length > 2 ? parts[2] / 3600 : 0);
        return string.Equals(reference?.Trim(), negative, StringComparison.OrdinalIgnoreCase) ? -degrees : degrees;
    }

    private static double? Positive(double? value) => value is > 0 ? value : null;

    /// <summary>The description of the photo's embedded ICC profile; null without one.</summary>
    private static string? ProfileName(string path)
    {
        IWICImagingFactory? factory = null;
        IWICBitmapDecoder? decoder = null;
        IWICBitmapFrameDecode? frame = null;
        IWICColorContext? context = null;
        try
        {
            factory = ComInterop.Create<IWICImagingFactory>(FactoryClass, typeof(IWICImagingFactory).GUID);
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
            nint contextPointer;
            if (factory.CreateColorContext(&contextPointer) < 0 || contextPointer == 0)
            {
                return null;
            }
            context = ComInterop.Wrap<IWICColorContext>(contextPointer);
            uint count;
            int type;
            uint size;
            if (frame.GetColorContexts(1, &contextPointer, &count) < 0 || count == 0
                || context.GetType(&type) < 0 || type != 1 || context.GetProfileBytes(0, null, &size) < 0 || size == 0)
            {
                return null;
            }
            var bytes = new byte[size];
            fixed (byte* buffer = bytes)
            {
                if (context.GetProfileBytes(size, buffer, &size) < 0)
                {
                    return null;
                }
            }
            return IccProfile.Description(bytes);
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            ComInterop.Release(context);
            ComInterop.Release(frame);
            ComInterop.Release(decoder);
            ComInterop.Release(factory);
        }
    }

    private static T? Value<T>(IPropertyStore store, PropertyKey? optionalKey, Func<PropVariant, T?> read)
    {
        if (optionalKey is not { } key)
        {
            return default;
        }
        var value = default(PropVariant);
        try
        {
            return store.GetValue(&key, &value) >= 0 ? read(value) : default;
        }
        finally
        {
            value.Clear();
        }
    }

    private static PropertyKey? Key(string name)
    {
        PropertyKey key;
        return PSGetPropertyKeyFromName(name, &key) >= 0 ? key : null;
    }

    [LibraryImport("propsys.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PSGetPropertyKeyFromName(string name, PropertyKey* key);
}

/// <summary>The name an ICC color profile gives itself (its <c>desc</c> tag, version 2 or 4).</summary>
public static class IccProfile
{
    public static string? Description(ReadOnlySpan<byte> profile)
    {
        if (profile.Length < 132)
        {
            return null;
        }
        var count = BigEndian(profile, 128);
        for (var i = 0; i < count && 132 + (i + 1) * 12 <= profile.Length; i++)
        {
            var entry = 132 + i * 12;
            if (BigEndian(profile, entry) != 0x64657363) // 'desc'
            {
                continue;
            }
            var offset = (int)BigEndian(profile, entry + 4);
            var size = (int)BigEndian(profile, entry + 8);
            if (offset < 0 || size < 12 || offset + size > profile.Length)
            {
                return null;
            }
            return Text(profile.Slice(offset, size));
        }
        return null;
    }

    private static string? Text(ReadOnlySpan<byte> tag)
    {
        var type = BigEndian(tag, 0);
        string? text = null;
        if (type == 0x64657363 && tag.Length >= 12) // 'desc': ASCII
        {
            var length = (int)Math.Min(BigEndian(tag, 8), (uint)(tag.Length - 12));
            text = System.Text.Encoding.ASCII.GetString(tag.Slice(12, length));
        }
        else if (type == 0x6D6C7563 && tag.Length >= 28) // 'mluc': the first record, UTF-16BE
        {
            var length = (int)BigEndian(tag, 20);
            var offset = (int)BigEndian(tag, 24);
            if (offset >= 0 && length >= 0 && offset + length <= tag.Length)
            {
                text = System.Text.Encoding.BigEndianUnicode.GetString(tag.Slice(offset, length));
            }
        }
        text = text?.TrimEnd('\0').Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static uint BigEndian(ReadOnlySpan<byte> bytes, int offset) =>
        offset + 4 <= bytes.Length ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]) : 0;
}
