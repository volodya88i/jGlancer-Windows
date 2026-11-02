using JGlancer.Core.Store;

namespace JGlancer.Core.Scanning;

/// <summary>Totals of one stage-1 scan over one or more directories.</summary>
public sealed class ScanReport
{
    public int Directories { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Moved { get; set; }
    public int Revived { get; set; }
    public int Missing { get; set; }
    public int Unchanged { get; set; }

    /// <summary>Directories that could not be listed (permissions, races); their rows were left untouched.</summary>
    public int UnreadableDirectories { get; set; }

    public TimeSpan Duration { get; set; }

    /// <summary>Whether the scan changed the database.</summary>
    public bool HasChanges => Added + Updated + Moved + Revived + Missing > 0;

    public void Add(DirectoryScanResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Directories++;
        Added += result.Added;
        Updated += result.Updated;
        Moved += result.Moved;
        Revived += result.Revived;
        Missing += result.Missing;
        Unchanged += result.Unchanged;
    }

    public void Add(ScanReport other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Directories += other.Directories;
        Added += other.Added;
        Updated += other.Updated;
        Moved += other.Moved;
        Revived += other.Revived;
        Missing += other.Missing;
        Unchanged += other.Unchanged;
        UnreadableDirectories += other.UnreadableDirectories;
        Duration += other.Duration;
    }
}

/// <summary>The root folder does not exist or is not a directory (unplugged drive, moved, deleted).</summary>
public sealed class RootUnavailableException(string path) : IOException($"folder unavailable: {path}")
{
    public string Path { get; } = path;
}

/// <summary>What a watched root's change batch did.</summary>
public sealed record WatchUpdate(IReadOnlyList<FileSystemEvent> Events, IReadOnlyList<RescanTarget> Targets, ScanReport Report);

/// <summary>Stage 2 for one file: dimensions, orientation, duration, codec, capture date (platform layer).</summary>
public interface IMetadataReader
{
    /// <summary>Reads <paramref name="item"/>; never throws (failures become <see cref="Model.MetadataState.Failed"/>).</summary>
    MetadataUpdate Read(PendingMetadataItem item);
}

/// <summary>How far a scan got: directories listed and media files found in them.</summary>
public readonly record struct ScanProgress(int Directories, int Files);
