using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JGlancer.Platform.Windows.Com;

namespace JGlancer.Platform.Windows;

/// <summary>
/// Video and audio facts from the Shell property system (Media Foundation property handlers): duration, frame size,
/// frame rate, rotation, codec, encode date. Formats without a handler (AIFF, most MKV/WebM/Ogg until libmpv in W4)
/// yield nothing.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class ShellMediaReader
{
    private static readonly PropertyKey? Duration = Key("System.Media.Duration");
    private static readonly PropertyKey? DateEncoded = Key("System.Media.DateEncoded");
    private static readonly PropertyKey? FrameWidth = Key("System.Video.FrameWidth");
    private static readonly PropertyKey? FrameHeight = Key("System.Video.FrameHeight");
    private static readonly PropertyKey? FrameRate = Key("System.Video.FrameRate");
    private static readonly PropertyKey? VideoCompression = Key("System.Video.Compression");
    private static readonly PropertyKey? VideoOrientation = Key("System.Video.Orientation");
    private static readonly PropertyKey? AudioFormat = Key("System.Audio.Format");

    /// <summary>What the property store knows about a media file.</summary>
    /// <param name="Duration">Seconds.</param>
    /// <param name="Rotation">Clockwise degrees the picture is shown rotated (0, 90, 180, 270).</param>
    public sealed record Facts(double? Duration, int? Width, int? Height, double? FrameRate, int Rotation, string? VideoCodec, string? AudioCodec, DateTimeOffset? Encoded);

    public static Facts? Read(string path)
    {
        var store = ComInterop.PropertyStore(path);
        if (store is null)
        {
            return null;
        }
        try
        {
            var duration = Integer(store, Duration);
            var width = Integer(store, FrameWidth);
            var height = Integer(store, FrameHeight);
            var rate = Integer(store, FrameRate);
            return new Facts(
                duration is > 0 ? duration.Value / 10_000_000d : null,
                width is > 0 ? (int)width : null,
                height is > 0 ? (int)height : null,
                rate is > 0 ? rate.Value / 1000d : null,
                (int)(Integer(store, VideoOrientation) ?? 0),
                Codecs.Video(Text(store, VideoCompression)),
                Codecs.Audio(Text(store, AudioFormat)),
                Date(store, DateEncoded));
        }
        finally
        {
            ComInterop.Release(store);
        }
    }

    private static long? Integer(IPropertyStore store, PropertyKey? key) => WithValue(store, key, v => v.Integer);

    private static string? Text(IPropertyStore store, PropertyKey? key) => WithValue(store, key, v => v.Text);

    private static DateTimeOffset? Date(IPropertyStore store, PropertyKey? key) => WithValue(store, key, v => v.Date);

    private static T? WithValue<T>(IPropertyStore store, PropertyKey? optionalKey, Func<PropVariant, T?> read)
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

    /// <summary>
    /// The key of a canonical property name, resolved by the system rather than hard-coded; null when this Windows
    /// version does not know the property (the reader then goes without it).
    /// </summary>
    private static PropertyKey? Key(string name)
    {
        PropertyKey key;
        return PSGetPropertyKeyFromName(name, &key) >= 0 ? key : null;
    }

    [LibraryImport("propsys.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PSGetPropertyKeyFromName(string name, PropertyKey* key);

    /// <summary>Media Foundation subtype GUIDs → the short codec names the macOS app stores.</summary>
    internal static class Codecs
    {
        /// <summary>Video subtypes carry a FourCC in their first group, e.g. <c>{34363248-…}</c> = "H264".</summary>
        public static string? Video(string? subtype)
        {
            if (FirstGroup(subtype) is not { } code)
            {
                return null;
            }
            var fourCC = string.Create(4, code, (chars, value) =>
            {
                for (var i = 0; i < 4; i++)
                {
                    chars[i] = (char)((value >> (8 * i)) & 0xFF);
                }
            });
            return fourCC.ToUpperInvariant() switch
            {
                "H264" or "AVC1" => "avc1",
                "HEVC" or "H265" or "HVC1" => "hvc1",
                var other => other.Trim().ToLowerInvariant(),
            };
        }

        /// <summary>Audio subtypes carry a WAVE_FORMAT tag in their first group.</summary>
        public static string? Audio(string? subtype) => FirstGroup(subtype) switch
        {
            null => null,
            0x0001 or 0x0003 => "lpcm",
            0x0055 => "mp3",
            0x1610 or 0x1600 => "aac",
            0x6C61 => "alac",
            0xF1AC => "flac",
            0x704F => "opus",
            0x2000 => "ac-3",
            0x2001 => "dts",
            0x0160 or 0x0161 or 0x0162 or 0x0163 => "wma",
            var tag => tag.Value.ToString("x4", CultureInfo.InvariantCulture),
        };

        private static uint? FirstGroup(string? guid) =>
            Guid.TryParse(guid, out var parsed)
                ? uint.Parse(parsed.ToString("D")[..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : null;
    }
}
