using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JGlancer.Core.Model;
using JGlancer.Core.Scanning;
using JGlancer.Core.Store;

namespace JGlancer.Platform.Windows;

/// <summary>
/// Stage 2 on Windows: photos through WIC (no pixel decoding), video and audio through the Shell property system.
/// Never throws; anything unreadable becomes <see cref="MetadataState.Failed"/> with its stage-1 facts kept.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMetadataReader : IMetadataReader
{
    public MetadataUpdate Read(PendingMetadataItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        try
        {
            return item.Kind == MediaKind.Photo ? Photo(item) : Media(item);
        }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or InvalidCastException)
        {
            return Failed(item);
        }
    }

    private static MetadataUpdate Photo(PendingMetadataItem item)
    {
        if (WicPhotoReader.Read(item.Path) is not { } facts)
        {
            return Failed(item);
        }
        var rotated = facts.Orientation is >= 5 and <= 8;
        return new MetadataUpdate(item.Id, item.ModifiedAt, MetadataState.Extracted)
        {
            Width = rotated ? facts.PixelHeight : facts.PixelWidth,
            Height = rotated ? facts.PixelWidth : facts.PixelHeight,
            Orientation = facts.Orientation,
            CapturedAt = ExifDate.Parse(facts.DateText, facts.OffsetText) ?? facts.PolicyDate,
        };
    }

    private static MetadataUpdate Media(PendingMetadataItem item)
    {
        if (ShellMediaReader.Read(item.Path) is not { Duration: not null } facts)
        {
            return Failed(item);
        }
        var sideways = facts.Rotation is 90 or 270;
        return new MetadataUpdate(item.Id, item.ModifiedAt, MetadataState.Extracted)
        {
            Width = sideways ? facts.Height : facts.Width,
            Height = sideways ? facts.Width : facts.Height,
            Duration = facts.Duration,
            FrameRate = facts.FrameRate,
            Codec = facts.VideoCodec ?? facts.AudioCodec,
            CapturedAt = facts.Encoded,
        };
    }

    private static MetadataUpdate Failed(PendingMetadataItem item) => new(item.Id, item.ModifiedAt, MetadataState.Failed);
}
