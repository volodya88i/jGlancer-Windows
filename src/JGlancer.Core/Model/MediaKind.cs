namespace JGlancer.Core.Model;

/// <summary>The kind of a media asset. Values are persisted in <c>asset.kind</c> and must never change.</summary>
public enum MediaKind
{
    /// <summary>Still images: JPEG, HEIC, PNG, TIFF, RAW, …</summary>
    Photo = 0,

    /// <summary>Movies.</summary>
    Video = 1,

    /// <summary>Audio files.</summary>
    Audio = 2,
}

/// <summary>Presence of an asset's file on disk. Values are persisted in <c>asset.state</c> and <c>folder.state</c>.</summary>
public enum AssetState
{
    /// <summary>The file exists where the library expects it.</summary>
    Present = 0,

    /// <summary>
    /// The file vanished. The row is kept as a tombstone so albums and flags survive a later reappearance or an
    /// external move (macOS ARCHITECTURE §13).
    /// </summary>
    Missing = 1,
}

/// <summary>Progress of stage-2 metadata extraction. Values are persisted in <c>asset.meta_state</c>.</summary>
public enum MetadataState
{
    /// <summary>Only stage-1 facts (name, size, dates, kind) are known.</summary>
    Pending = 0,

    /// <summary>Dimensions, duration, capture date etc. were extracted.</summary>
    Extracted = 1,

    /// <summary>Extraction failed (corrupt or unsupported file); stage-1 facts remain valid.</summary>
    Failed = 2,
}

/// <summary>A file's flag (macOS D-57): a colored star, or none. Values are stored in <c>asset.flag</c>.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "\"Flag\" is the product's term for the colored star, not a [Flags] enum.")]
public enum AssetFlag
{
    None = 0,

    /// <summary>The app's accent color.</summary>
    Accent = 1,
    Red = 2,
    Orange = 3,
    Yellow = 4,
    Green = 5,
    Blue = 6,
    Purple = 7,
}

/// <summary>Flag helpers.</summary>
public static class AssetFlags
{
    /// <summary>The colors of F then 1 … 6, in that order (W-05).</summary>
    public static IReadOnlyList<AssetFlag> Colors { get; } =
        [AssetFlag.Red, AssetFlag.Orange, AssetFlag.Yellow, AssetFlag.Green, AssetFlag.Blue, AssetFlag.Purple];

    /// <summary>
    /// What pressing <paramref name="pressed"/> gives files flagged <paramref name="current"/>: if every one already has
    /// it, the flag comes off; otherwise every file gets it. <see cref="AssetFlag.None"/> always removes.
    /// </summary>
    public static AssetFlag Applying(AssetFlag pressed, IReadOnlyCollection<AssetFlag> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (pressed == AssetFlag.None || current.Count == 0)
        {
            return pressed;
        }
        return current.All(flag => flag == pressed) ? AssetFlag.None : pressed;
    }
}
