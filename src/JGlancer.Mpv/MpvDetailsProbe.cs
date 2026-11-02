using System.Globalization;
using JGlancer.Core.Model;

namespace JGlancer.Mpv;

/// <summary>
/// Extended facts of video and audio files Windows has no property handler for (AIFF, Matroska, WebM, Ogg …): a
/// silent, headless mpv opens the file paused and reads its tracks as the demuxer sees them. Nothing is decoded.
/// </summary>
public static class MpvDetailsProbe
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    public static async Task<MediaDetails> ReadAsync(string path, CancellationToken cancellation)
    {
        var loaded = new TaskCompletionSource<MediaDetails>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var player = new MpvPlayer([("ao", "null"), ("vo", "null")], withVideo: true);
        player.FileLoaded += () => loaded.TrySetResult(Tracks(player));
        player.Ended += _ => loaded.TrySetResult(MediaDetails.Empty);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(Limit);
        using var registration = timeout.Token.Register(() => loaded.TrySetResult(MediaDetails.Empty));
        player.Load(path, paused: true);
        return await loaded.Task.ConfigureAwait(false);
    }

    /// <summary>The first video and the first audio track of the loaded file.</summary>
    private static MediaDetails Tracks(MpvPlayer player)
    {
        var details = MediaDetails.Empty;
        var count = Integer(player.GetString("track-list/count")) ?? 0;
        bool video = false, audio = false;
        for (var i = 0; i < count; i++)
        {
            var track = $"track-list/{i}/";
            var type = player.GetString(track + "type");
            var codec = player.GetString(track + "codec");
            // Cover art comes as an "image" video track: not a video.
            if (type == "video" && !video && player.GetString(track + "image") != "yes" && codec is not null)
            {
                video = true;
                details = details with
                {
                    VideoCodec = MediaDetails.CodecName(codec),
                    FrameRate = Positive(Number(player.GetString(track + "demux-fps"))),
                    VideoBitrate = Positive(Number(player.GetString(track + "demux-bitrate"))),
                };
            }
            else if (type == "audio" && !audio && codec is not null)
            {
                audio = true;
                details = details with
                {
                    AudioCodec = MediaDetails.CodecName(codec),
                    AudioBitrate = Positive(Number(player.GetString(track + "demux-bitrate"))),
                    SampleRate = Positive(Number(player.GetString(track + "demux-samplerate"))),
                    Channels = Integer(player.GetString(track + "demux-channel-count")) is > 0 and var channels ? (int)channels : null,
                    BitDepth = PcmDepth(codec),
                };
            }
        }
        return details;
    }

    /// <summary>"pcm_s24le" → 24; FFmpeg's PCM names carry the depth, other codecs have none.</summary>
    internal static int? PcmDepth(string codec)
    {
        if (!codec.StartsWith("pcm_", StringComparison.Ordinal))
        {
            return null;
        }
        var digits = new string([.. codec.Skip(5).TakeWhile(char.IsAsciiDigit)]);
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var depth) && depth > 0 ? depth : null;
    }

    private static long? Integer(string? text) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static double? Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static double? Positive(double? value) => value is > 0 ? value : null;
}
