using System.Globalization;
using System.Text.RegularExpressions;

namespace JGlancer.Core.Model;

/// <summary>A photo of a folder, as sequence detection sees it.</summary>
public readonly record struct SequenceCandidate(long Id, long FolderId, string Name);

/// <summary>An image sequence: frames numbered one after another ("shot_001.png" … "shot_090.png"), in frame order.</summary>
public sealed record ImageSequence(IReadOnlyList<long> Frames)
{
    /// <summary>The first frame: the stack's face in the grid.</summary>
    public long Head => Frames[0];
}

/// <summary>
/// Finds image sequences (W-81): photos of one folder whose names differ only in a frame number
/// ("render_0001.exr" … "render_0240.exr"), at least <see cref="MinimumFrames"/> consecutive numbers.
/// Camera names (IMG_, DSC_, PXL_, …) are left out: a run of camera shots is a series of photos, not frames.
/// </summary>
public static partial class ImageSequences
{
    /// <summary>Fewer consecutive frames than this are ordinary files.</summary>
    public const int MinimumFrames = 5;

    // Cameras and phones number their shots too; those stay single photos.
    private static readonly string[] CameraPrefixes =
        ["img", "dsc", "dscn", "dscf", "dji", "gopr", "gp", "gh", "gx", "pxl", "mvi", "vid", "cimg", "sam", "pano", "hpim", "photo", "screenshot", "image"];

    [GeneratedRegex(@"^(?<prefix>.*?)(?<number>\d+)(?<extension>\.[A-Za-z0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    public static IReadOnlyList<ImageSequence> Find(IEnumerable<SequenceCandidate> photos)
    {
        ArgumentNullException.ThrowIfNull(photos);
        var parts = new Dictionary<(long Folder, string Prefix, string Extension), List<(long Number, long Id)>>();
        foreach (var photo in photos)
        {
            var match = NamePattern().Match(photo.Name);
            if (!match.Success)
            {
                continue;
            }
            var prefix = match.Groups["prefix"].Value;
            var digits = match.Groups["number"].Value;
            if (digits.Length > 9 || IsCameraName(prefix) || !long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                continue;
            }
            // Not by the number's width: "frame9" and "frame10" belong together.
            var key = (photo.FolderId, prefix.ToUpperInvariant(), match.Groups["extension"].Value.ToUpperInvariant());
            if (!parts.TryGetValue(key, out var list))
            {
                parts[key] = list = [];
            }
            list.Add((number, photo.Id));
        }
        var sequences = new List<ImageSequence>();
        foreach (var frames in parts.Values)
        {
            if (frames.Count < MinimumFrames)
            {
                continue;
            }
            frames.Sort((a, b) => a.Number.CompareTo(b.Number));
            // Runs of consecutive numbers: a gap starts another sequence.
            var run = new List<long> { frames[0].Id };
            for (var i = 1; i <= frames.Count; i++)
            {
                if (i < frames.Count && frames[i].Number == frames[i - 1].Number + 1)
                {
                    run.Add(frames[i].Id);
                    continue;
                }
                if (run.Count >= MinimumFrames)
                {
                    sequences.Add(new ImageSequence(run));
                }
                if (i < frames.Count)
                {
                    run = [frames[i].Id];
                }
            }
        }
        return sequences;
    }

    private static bool IsCameraName(string prefix)
    {
        var name = prefix.TrimEnd('_', '-', ' ', '.').ToLowerInvariant();
        return CameraPrefixes.Contains(name);
    }
}
