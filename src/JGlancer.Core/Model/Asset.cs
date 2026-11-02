namespace JGlancer.Core.Model;

/// <summary>A media file known to the library: the full row, fetched lazily for visible items.</summary>
/// <param name="Path">Absolute path of the file.</param>
/// <param name="TypeIdentifier">Type identifier derived from the extension, e.g. <c>public.jpeg</c>.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Width">Display width (photo orientation and video rotation applied); null until stage 2.</param>
/// <param name="Height">Display height; null until stage 2.</param>
/// <param name="Orientation">EXIF orientation (1–8) of photos.</param>
/// <param name="Duration">Seconds, for video and audio.</param>
/// <param name="Codec">Codec code or short name, e.g. <c>avc1</c>, <c>hevc</c>, <c>aac</c>, <c>lpcm</c>.</param>
public sealed record Asset(
    long Id,
    long FolderId,
    string Name,
    string Path,
    MediaKind Kind,
    string? TypeIdentifier,
    long Size,
    DateTimeOffset ModifiedAt,
    DateTimeOffset? CreatedAt,
    int? Width,
    int? Height,
    int? Orientation,
    double? Duration,
    double? FrameRate,
    string? Codec,
    DateTimeOffset? CapturedAt,
    AssetState State,
    MetadataState MetadataState,
    AssetFlag Flag);

/// <summary>
/// The lean per-item entry the grid holds for a whole query (macOS ARCHITECTURE §4). The list order is the sort order;
/// full rows are fetched only for visible items.
/// </summary>
public readonly record struct AssetRef(long Id, MediaKind Kind);

/// <summary>A directory inside a root folder (the root itself has an empty <see cref="RelativePath"/>).</summary>
/// <param name="RelativePath">Path relative to the root, <c>/</c>-separated, empty for the root itself.</param>
/// <param name="Name">Last path component (the root folder's own name for the root).</param>
public sealed record Folder(long Id, long RootId, long? ParentId, string RelativePath, string Name, AssetState State);

/// <summary>A folder the user added to the library. Files inside are indexed in place, never imported.</summary>
/// <param name="Path">Absolute canonical path (final path, junctions resolved).</param>
/// <param name="VolumeId">Serial number of the volume that holds the folder (file ids are per volume).</param>
public sealed record RootFolder(long Id, string Path, string? VolumeId, bool IsRecursive, bool IsWatched, DateTimeOffset AddedAt);

/// <summary>Dates are stored as integer milliseconds since 1970 (macOS D-29): exact equality for change detection.</summary>
public static class UnixMilliseconds
{
    public static long From(DateTimeOffset date) => date.ToUnixTimeMilliseconds();

    public static DateTimeOffset ToDate(long milliseconds) => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
}
