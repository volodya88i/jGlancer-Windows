using System.Globalization;
using System.Runtime.Versioning;
using JGlancer.Core.Model;
using JGlancer.Core.Playback;
using JGlancer.Mpv;

namespace JGlancer.Platform.Windows;

/// <summary>
/// Saves segments (macOS SegmentExporter, D-73, D-74; W-40, W-106). Video: encoded again on the exact range by Windows'
/// own H.264 encoder through mpv, or the streams copied from the keyframe at or before the start into MP4 (Media
/// Foundation, nothing re-encoded). Audio: decoded by mpv with an exact trim, the speed
/// with or without Keep Pitch, and 30 ms fades; cut to the exact length (mpv's encoder pads its last chunk with
/// silence); then WAV and AIFF are written here, FLAC and M4A (AAC 256 kbit/s, ALAC for lossless sources) encoded by
/// Media Foundation. WAV, AIFF and FLAC keep their format and depth.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SegmentExporter
{
    private static readonly string[] LosslessCodecs = ["flac", "alac", "wavpack", "tta", "ape", "truehd", "mlp"];

    /// <summary>Writes the segment to <paramref name="target"/> (which must not exist yet).</summary>
    public static async Task ExportAsync(SegmentExportRequest request, string target, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Segments.CanExport(request.Source, request.Kind, request.ExactVideo) || request.Range.Length <= 0)
        {
            throw new NotSupportedException("this file cannot be cut");
        }
        if (request.Kind == MediaKind.Video)
        {
            if (request.ExactVideo)
            {
                if (await MpvEncoder.EncodeAsync(request.Source, target, ExactVideoOptions(request.Range), cancellation).ConfigureAwait(false))
                {
                    return;
                }
                cancellation.ThrowIfCancellationRequested();
                // Windows without its media features has no encoder: a copy is still a good segment.
                if (!Segments.CanCopyVideo(request.Source))
                {
                    throw new IOException("the video could not be encoded");
                }
                File.Delete(target);
            }
            await Task.Run(() => MediaFoundation.CopyVideoSegment(request.Source, target, request.Range.Start, request.Range.End), cancellation)
                .ConfigureAwait(false);
            return;
        }
        await ExportAudioAsync(request, target, cancellation).ConfigureAwait(false);
    }

    /// <summary>
    /// Exports into a temporary folder, then moves the file to <paramref name="target"/>, or to "Name 2.ext" … when
    /// something is already there (nothing is replaced). Returns where the file went.
    /// </summary>
    public static async Task<string> SaveAsync(SegmentExportRequest request, string target, CancellationToken cancellation)
    {
        var folder = TemporaryFolder();
        try
        {
            var temporary = Path.Combine(folder, Path.GetFileName(target));
            await ExportAsync(request, temporary, cancellation).ConfigureAwait(false);
            var destination = Segments.Vacant(target, File.Exists);
            File.Move(temporary, destination);
            return destination;
        }
        finally
        {
            RemoveTemporaryFolder(folder);
        }
    }

    /// <summary>A new, empty folder of our own in %TEMP% (only folders made here are ever removed).</summary>
    public static string TemporaryFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "jGlancer-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Removes a folder made by <see cref="TemporaryFolder"/>; anything else is left alone.</summary>
    public static void RemoveTemporaryFolder(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
        if (!name.StartsWith("jGlancer-export-", StringComparison.Ordinal)
            || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)),
                Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Removes export folders an earlier run left in %TEMP% (the app quit while a dropped file might still be read),
    /// when they are older than an hour. Only folders named by <see cref="TemporaryFolder"/> are touched.
    /// </summary>
    public static void RemoveLeftovers()
    {
        foreach (var folder in Directory.EnumerateDirectories(Path.GetTempPath(), "jGlancer-export-*"))
        {
            if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(folder) > TimeSpan.FromHours(1))
            {
                RemoveTemporaryFolder(folder);
            }
        }
    }

    /// <summary>Quality 85 keeps the picture indistinguishable from the source; the software encoder is on every Windows with media features.</summary>
    private static List<(string, string)> ExactVideoOptions(SegmentRange range) =>
    [
        ("start", Seconds(range.Start)),
        ("end", Seconds(range.End)),
        ("sid", "no"),
        ("of", "mp4"),
        ("ofopts", "movflags=+faststart"),
        ("ovc", "h264_mf"),
        ("ovcopts", "rate_control=quality,quality=85,hw_encoding=0"),
        ("oac", "aac"),
        ("oacopts", "b=256k"),
    ];

    private static async Task ExportAudioAsync(SegmentExportRequest request, string target, CancellationToken cancellation)
    {
        var facts = await MpvAudioDecoder.ProbeAsync(request.Source, cancellation).ConfigureAwait(false)
            ?? throw new NotSupportedException("no audio");
        var extension = Segments.FileExtension(request);
        var sourceBits = SourceBits(facts);
        var lossless = facts.Codec.StartsWith("pcm_", StringComparison.Ordinal) || LosslessCodecs.Contains(facts.Codec);
        var encoding = extension switch
        {
            "flac" => AudioEncoding.Flac,
            "mp3" => AudioEncoding.Mp3,
            "m4a" when lossless => AudioEncoding.Alac,
            "m4a" => AudioEncoding.Aac,
            _ => (AudioEncoding?)null,
        };
        // What the decode step writes: the source's depth for the lossless formats; 16-bit at 44.1 or 48 kHz for AAC and
        // MP3, which take nothing else; at most two channels in M4A (macOS) and MP3.
        var lossy = encoding is AudioEncoding.Aac or AudioEncoding.Mp3;
        var decodeCodec = lossy ? "pcm_s16le" : PcmCodec(facts, sourceBits);
        var options = new List<(string, string)>
        {
            ("start", Seconds(request.Range.Start)),
            ("end", Seconds(request.Range.End + 0.5)),
            ("vid", "no"),
            ("sid", "no"),
            ("of", "wav"),
            ("oac", decodeCodec),
            ("speed", Seconds(request.Rate)),
            ("audio-pitch-correction", request.PreservesPitch ? "yes" : "no"),
            ("af", Filters(request)),
        };
        if (lossy && facts.SampleRate is not (44100 or 48000))
        {
            options.Add(("audio-samplerate", "48000"));
        }
        if (extension is "m4a" or "mp3" && facts.Channels > 2)
        {
            options.Add(("audio-channels", "stereo"));
        }
        var folder = TemporaryFolder();
        try
        {
            var decoded = Path.Combine(folder, "decoded.wav");
            if (!await MpvEncoder.EncodeAsync(request.Source, decoded, options, cancellation).ConfigureAwait(false))
            {
                throw new IOException("the audio could not be decoded");
            }
            var pcm = PcmFiles.ReadWave(await File.ReadAllBytesAsync(decoded, cancellation).ConfigureAwait(false))
                ?? throw new IOException("the decoded audio is unreadable");
            // mpv pads its last chunk with silence: the exact length of the segment at the chosen speed.
            pcm = pcm.Truncated((long)Math.Round(request.Range.Length / request.Rate * pcm.SampleRate));
            switch (encoding)
            {
                case { } with:
                    await Task.Run(() => MediaFoundation.EncodeAudio(pcm, target, with), cancellation).ConfigureAwait(false);
                    break;
                default:
                    var bytes = extension is "aif" or "aiff" ? PcmFiles.WriteAiff(pcm) : PcmFiles.WriteWave(pcm);
                    await File.WriteAllBytesAsync(target, bytes, cancellation).ConfigureAwait(false);
                    break;
            }
        }
        finally
        {
            RemoveTemporaryFolder(folder);
        }
    }

    /// <summary>The exact cut and the fades, on the source's own timestamps (mpv does not restart them at the cut).</summary>
    private static string Filters(SegmentExportRequest request)
    {
        var start = Seconds(request.Range.Start);
        var end = Seconds(request.Range.End);
        var filters = $"atrim=start={start}:end={end}";
        if (request.Fades && request.Range.Length > Segments.FadeSeconds * 2)
        {
            var fade = Seconds(Segments.FadeSeconds);
            filters += $",afade=t=in:st={start}:d={fade},afade=t=out:st={Seconds(request.Range.End - Segments.FadeSeconds)}:d={fade}";
        }
        var stretch = PlaybackSpeed.StretchFilter(request.Rate, request.PreservesPitch);
        return stretch.Length == 0 ? $"lavfi=[{filters}]" : $"lavfi=[{filters}],{stretch}";
    }

    /// <summary>Bits per sample of the source: from a PCM codec's name, else 24 for formats decoded to 32-bit, else 16.</summary>
    private static int SourceBits(MpvAudioDecoder.AudioFacts facts)
    {
        if (facts.Codec.Contains("24", StringComparison.Ordinal))
        {
            return 24;
        }
        if (facts.Codec.Contains("32", StringComparison.Ordinal) || facts.Codec.Contains("f64", StringComparison.Ordinal))
        {
            return 32;
        }
        return facts.SampleFormat.StartsWith("s32", StringComparison.Ordinal) || facts.SampleFormat.StartsWith("float", StringComparison.Ordinal) ? 24 : 16;
    }

    /// <summary>The PCM codec mpv decodes to: float stays float, otherwise the source's depth.</summary>
    private static string PcmCodec(MpvAudioDecoder.AudioFacts facts, int bits) =>
        facts.Codec.Contains("f32", StringComparison.Ordinal) || facts.Codec.Contains("f64", StringComparison.Ordinal)
            ? "pcm_f32le"
            : bits switch
            {
                >= 32 => "pcm_s32le",
                24 => "pcm_s24le",
                _ => "pcm_s16le",
            };

    private static string Seconds(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
