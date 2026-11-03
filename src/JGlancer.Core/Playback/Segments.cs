using System.Globalization;
using JGlancer.Core.Model;

namespace JGlancer.Core.Playback;

/// <summary>A part of a file, in seconds of the source.</summary>
public readonly record struct SegmentRange(double Start, double End)
{
    public double Length => End - Start;

    public bool Contains(double time) => time >= Start && time <= End;
}

/// <summary>A part of a video or audio file to save as a new file (macOS SegmentExportRequest, D-73, D-74).</summary>
/// <param name="Rate">Audio only: the playback speed applied to the saved file.</param>
/// <param name="Fades">Audio only: 30 ms fades at both ends (no click where the cut falls mid-wave).</param>
/// <param name="KeepsAudioFormat">WAV, AIFF and FLAC stay in their format; off (or any other source): M4A.</param>
/// <param name="IsWholeFile">The range is all of the file: the saved name carries no range.</param>
/// <param name="ExactVideo">Video only: encoded again to start and end exactly on the range; off: copied from the keyframe at or before its start.</param>
public sealed record SegmentExportRequest(
    string Source,
    MediaKind Kind,
    SegmentRange Range,
    double Rate = PlaybackSpeed.Normal,
    bool PreservesPitch = true,
    bool Fades = false,
    bool KeepsAudioFormat = true,
    bool ExactVideo = false,
    bool IsWholeFile = false);

/// <summary>Rules of segment selection and export that do not depend on the platform (macOS SegmentExporter).</summary>
public static class Segments
{
    /// <summary>Fades at both ends of an exported audio segment.</summary>
    public const double FadeSeconds = 0.03;

    /// <summary>The shortest segment.</summary>
    public const double MinimumLength = 0.1;

    /// <summary>How near the end bracket the playhead counts as stopped there.</summary>
    public const double EndTolerance = 0.1;

    private static readonly string[] CopyableVideo = ["mp4", "mov", "m4v"];

    /// <summary>A new segment: a tenth of the file (1 … 30 s) from the playhead, inside the file.</summary>
    public static SegmentRange Default(double duration, double playhead)
    {
        var length = Math.Min(Math.Clamp(duration * 0.1, 1, 30), duration);
        var start = Math.Clamp(playhead, 0, Math.Max(0, duration - length));
        return new SegmentRange(start, start + length);
    }

    /// <summary>
    /// Whether a file can be cut: audio always (it is decoded and encoded again); video always when encoded again
    /// (<paramref name="exactVideo"/>), else when its streams can be copied into MP4 (MP4, MOV, M4V — not Matroska, WebM, AVI…).
    /// </summary>
    public static bool CanExport(string path, MediaKind kind, bool exactVideo = false) => kind switch
    {
        MediaKind.Audio => true,
        MediaKind.Video => exactVideo || CanCopyVideo(path),
        _ => false,
    };

    /// <summary>Whether a video's streams can be copied into MP4 without encoding them again.</summary>
    public static bool CanCopyVideo(string path) => CopyableVideo.Contains(Extension(path));

    /// <summary>
    /// The saved file's extension: video becomes MP4 (Windows writes no QuickTime files; MP4 plays everywhere);
    /// audio keeps WAV, AIFF, FLAC and MP3 when <see cref="SegmentExportRequest.KeepsAudioFormat"/>, else M4A.
    /// </summary>
    public static string FileExtension(SegmentExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind == MediaKind.Video)
        {
            return "mp4";
        }
        var kept = Extension(request.Source) switch
        {
            "wav" or "wave" => "wav",
            "aif" => "aif",
            "aiff" or "aifc" => "aiff",
            "flac" => "flac",
            "mp3" => "mp3",
            _ => null,
        };
        return request.KeepsAudioFormat ? kept ?? "m4a" : "m4a";
    }

    /// <summary>"Name (1m02.0s–1m15.4s).ext", with the speed for audio when it is not 1× ("Name (…) 1.25x.m4a").</summary>
    public static string SuggestedName(SegmentExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = Path.GetFileNameWithoutExtension(request.Source);
        if (!request.IsWholeFile)
        {
            name += $" ({Stamp(request.Range.Start)}–{Stamp(request.Range.End)})";
        }
        if (request.Kind == MediaKind.Audio && Math.Abs(request.Rate - PlaybackSpeed.Normal) > 0.001)
        {
            name += " " + request.Rate.ToString("0.##", CultureInfo.InvariantCulture) + "x";
        }
        return name + "." + FileExtension(request);
    }

    /// <summary><c>1m02.0s</c>.</summary>
    public static string Stamp(double seconds)
    {
        var tenths = (long)Math.Round(Math.Max(seconds, 0) * 10);
        return string.Create(CultureInfo.InvariantCulture, $"{tenths / 600}m{tenths / 10 % 60:00}.{tenths % 10}s");
    }

    /// <summary><c>1:02.3–1:15.0</c> for the controls row.</summary>
    public static string Label(SegmentRange range) => $"{DurationFormat.Tenths(range.Start)}–{DurationFormat.Tenths(range.End)}";

    /// <summary><paramref name="path"/>, or the first "Name N.ext" next to it that does not exist (nothing is replaced).</summary>
    public static string Vacant(string path, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        if (!exists(path))
        {
            return path;
        }
        var folder = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var number = 2; ; number++)
        {
            var candidate = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{name} {number}{extension}"));
            if (!exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string Extension(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
}
