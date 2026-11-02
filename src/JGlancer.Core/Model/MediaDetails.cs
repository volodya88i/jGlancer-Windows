using System.Globalization;
using JGlancer.Core.Localization;

namespace JGlancer.Core.Model;

/// <summary>
/// The extended facts of one file for Get Info and, if turned on, the preview pane (macOS MediaDetails, FEATURES
/// §12a): camera and exposure of photos, codecs and rates of video and audio. Read from the file on request, never
/// stored. Values are raw (seconds, bits per second, degrees); <see cref="MediaDetailsRows"/> formats and labels them.
/// </summary>
public sealed record MediaDetails
{
    // Photo
    public string? Camera { get; init; }
    public string? Lens { get; init; }

    /// <summary>Millimeters.</summary>
    public double? FocalLength { get; init; }

    /// <summary>The f-number.</summary>
    public double? Aperture { get; init; }

    /// <summary>Seconds.</summary>
    public double? ExposureTime { get; init; }
    public int? Iso { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }

    /// <summary>Meters above sea level.</summary>
    public double? Altitude { get; init; }
    public string? ColorProfile { get; init; }

    /// <summary>Bits per pixel of a photo, or per sample of audio.</summary>
    public int? BitDepth { get; init; }

    // Video and audio
    public string? VideoCodec { get; init; }
    public double? FrameRate { get; init; }

    /// <summary>Bits per second.</summary>
    public double? VideoBitrate { get; init; }
    public string? AudioCodec { get; init; }

    /// <summary>Bits per second.</summary>
    public double? AudioBitrate { get; init; }

    /// <summary>Hertz.</summary>
    public double? SampleRate { get; init; }
    public int? Channels { get; init; }

    public static MediaDetails Empty { get; } = new();

    /// <summary>True when nothing was found.</summary>
    public bool IsEmpty => this == Empty;

    /// <summary>
    /// "Apple iPhone 15 Pro" from make "Apple" and model "iPhone 15 Pro"; a model that already names the maker
    /// ("Canon EOS R5", "NIKON Z 6" for "NIKON CORPORATION") is used as it is.
    /// </summary>
    public static string? CameraName(string? make, string? model)
    {
        make = make?.Trim() ?? "";
        model = model?.Trim() ?? "";
        if (model.Length == 0)
        {
            return make.Length == 0 ? null : make;
        }
        var maker = make.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return make.Length == 0 || model.StartsWith(maker, StringComparison.OrdinalIgnoreCase) ? model : make + " " + model;
    }

    /// <summary>A codec's common name from a four-character code or a short tag ("avc1" → "H.264"); unknown ones as they are.</summary>
    public static string CodecName(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return CodecNames.GetValueOrDefault(code.Trim().ToLowerInvariant(), code.Trim());
    }

    private static readonly Dictionary<string, string> CodecNames = new()
    {
        ["avc1"] = "H.264", ["avc3"] = "H.264", ["h264"] = "H.264", ["hvc1"] = "HEVC", ["hev1"] = "HEVC", ["hevc"] = "HEVC",
        ["h265"] = "HEVC", ["av01"] = "AV1", ["av1"] = "AV1", ["vp09"] = "VP9", ["vp9"] = "VP9", ["vp8"] = "VP8",
        ["apch"] = "ProRes 422 HQ", ["apcn"] = "ProRes 422", ["apcs"] = "ProRes 422 LT", ["apco"] = "ProRes 422 Proxy",
        ["ap4h"] = "ProRes 4444", ["ap4x"] = "ProRes 4444 XQ", ["prores"] = "ProRes", ["jpeg"] = "Motion JPEG",
        ["mjpg"] = "Motion JPEG", ["mjpeg"] = "Motion JPEG", ["mp4v"] = "MPEG-4", ["mpeg4"] = "MPEG-4", ["mpeg2video"] = "MPEG-2",
        ["wmv3"] = "WMV", ["wvc1"] = "VC-1", ["vc1"] = "VC-1",
        ["aac"] = "AAC", ["mp4a"] = "AAC", ["alac"] = "ALAC", [".mp3"] = "MP3", ["mp3"] = "MP3", ["mp3float"] = "MP3",
        ["flac"] = "FLAC", ["opus"] = "Opus", ["vorbis"] = "Vorbis", ["lpcm"] = "PCM", ["sowt"] = "PCM", ["twos"] = "PCM",
        ["ac-3"] = "AC-3", ["ac3"] = "AC-3", ["ec-3"] = "E-AC-3", ["eac3"] = "E-AC-3", ["dts"] = "DTS", ["wma"] = "WMA",
        ["wmav2"] = "WMA", ["wmapro"] = "WMA Pro", ["pcm_s16le"] = "PCM", ["pcm_s24le"] = "PCM", ["pcm_s32le"] = "PCM",
        ["pcm_s16be"] = "PCM", ["pcm_s24be"] = "PCM", ["pcm_f32le"] = "PCM", ["pcm_f32be"] = "PCM", ["pcm_u8"] = "PCM",
    };
}

/// <summary>The extended facts as label / value rows, in the app's language and the culture's numbers (macOS MediaDetailsRows).</summary>
public static class MediaDetailsRows
{
    public static IReadOnlyList<(string Label, string Value)> Rows(MediaDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var rows = new List<(string Label, string Value)>();
        void Add(string label, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                rows.Add((label, value));
            }
        }
        var media = details.VideoCodec is not null || details.AudioCodec is not null;
        Add(L.T("Camera"), details.Camera);
        Add(L.T("Lens"), details.Lens);
        Add(L.T("Focal Length"), details.FocalLength is { } focal ? L.T("{0} mm", Number(focal, 1)) : null);
        Add(L.T("Aperture"), details.Aperture is { } aperture ? "ƒ/" + Number(aperture, 1) : null);
        Add(L.T("Exposure"), details.ExposureTime is { } exposure ? Exposure(exposure) : null);
        Add(L.T("ISO"), details.Iso?.ToString(CultureInfo.CurrentCulture));
        if (details is { Latitude: { } latitude, Longitude: { } longitude })
        {
            Add(L.T("Location"), string.Create(CultureInfo.InvariantCulture, $"{latitude:F5}, {longitude:F5}"));
        }
        Add(L.T("Altitude"), details.Altitude is { } altitude ? L.T("{0} m", Number(altitude, 0)) : null);
        Add(L.T("Color Profile"), details.ColorProfile);
        if (!media)
        {
            Add(L.T("Bit Depth"), details.BitDepth is { } depth ? L.T("{0} bit", depth) : null);
        }
        Add(L.T("Video"), details.VideoCodec);
        Add(L.T("Frame Rate"), details.FrameRate is { } rate ? L.T("{0} fps", Number(rate, 2)) : null);
        Add(L.T("Video Bitrate"), details.VideoBitrate is { } videoBitrate ? Bitrate(videoBitrate) : null);
        Add(L.T("Audio"), details.AudioCodec);
        Add(L.T("Audio Bitrate"), details.AudioBitrate is { } audioBitrate ? Bitrate(audioBitrate) : null);
        Add(L.T("Sample Rate"), details.SampleRate is { } sampleRate ? L.T("{0} kHz", Number(sampleRate / 1000, 1)) : null);
        Add(L.T("Channels"), details.Channels switch
        {
            null => null,
            1 => L.T("Mono"),
            2 => L.T("Stereo"),
            var count => L.T("{0} channels", count),
        });
        if (details.AudioCodec is not null)
        {
            Add(L.T("Bit Depth"), details.BitDepth is { } depth ? L.T("{0} bit", depth) : null);
        }
        return rows;
    }

    private static string Exposure(double seconds) =>
        seconds is > 0 and < 1
            ? L.T("1/{0} s", (int)Math.Round(1 / seconds))
            : L.T("{0} s", Number(seconds, 1));

    private static string Bitrate(double bitsPerSecond) =>
        bitsPerSecond >= 1_000_000
            ? L.T("{0} Mbit/s", Number(bitsPerSecond / 1_000_000, 1))
            : L.T("{0} kbit/s", Number(bitsPerSecond / 1000, 0));

    /// <summary>At most <paramref name="digits"/> decimals, trailing zeros dropped (macOS fractionLength 0…n).</summary>
    private static string Number(double value, int digits) =>
        Math.Round(value, digits).ToString("0." + new string('#', digits), CultureInfo.CurrentCulture).TrimEnd('.');
}
