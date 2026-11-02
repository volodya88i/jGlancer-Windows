namespace JGlancer.Core.Model;

/// <summary>A virtual album: an ordered list of references to library assets; files stay where they are (FEATURES §6).</summary>
/// <param name="CoverAssetId">The chosen cover, else the first present item; null for an empty album.</param>
/// <param name="ItemCount">Present items (files that vanished are not counted).</param>
public sealed record Album(long Id, string Name, DateTimeOffset CreatedAt, long? CoverAssetId, int ItemCount);

/// <summary>An album item as it was, for undoing its removal.</summary>
public readonly record struct AlbumItemPosition(long AssetId, long Position);
