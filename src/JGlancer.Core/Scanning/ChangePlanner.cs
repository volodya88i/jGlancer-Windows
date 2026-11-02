using JGlancer.Core.Store;

namespace JGlancer.Core.Scanning;

/// <summary>What happened to a path (FileSystemWatcher change types, plus a lost-events marker).</summary>
public enum FileSystemChange
{
    Created,
    Changed,
    Deleted,
    Renamed,

    /// <summary>The watcher's buffer overflowed or it failed: events were lost and the tree must be rescanned.</summary>
    Overflow,
}

/// <summary>One file-system event.</summary>
/// <param name="OldPath">The previous path of a rename.</param>
/// <param name="IsDirectory">Whether <paramref name="Path"/> was a directory when the event arrived (unknown for deletions).</param>
public sealed record FileSystemEvent(FileSystemChange Change, string Path, string? OldPath = null, bool IsDirectory = false);

/// <summary>A directory to rescan after file-system events.</summary>
/// <param name="RelativePath">Path relative to the root (<c>/</c>-separated), empty for the root.</param>
/// <param name="Recursive">Whether subdirectories are rescanned too.</param>
public sealed record RescanTarget(string RelativePath, bool Recursive);

/// <summary>Turns a batch of events into the smallest set of directory rescans (pure; no IO).</summary>
public static class ChangePlanner
{
    /// <summary>Rescans needed for <paramref name="events"/> under <paramref name="rootPath"/>. Events in hidden items are ignored.</summary>
    public static IReadOnlyList<RescanTarget> Plan(IReadOnlyList<FileSystemEvent> events, string rootPath)
    {
        ArgumentNullException.ThrowIfNull(events);
        var targets = new HashSet<RescanTarget>();
        foreach (var change in events)
        {
            if (change.Change == FileSystemChange.Overflow)
            {
                return [new RescanTarget("", Recursive: true)];
            }
            foreach (var path in change.OldPath is null ? [change.Path] : new[] { change.Path, change.OldPath })
            {
                if (LibraryPaths.Relative(path, rootPath) is not { } relative || IsHidden(relative))
                {
                    continue;
                }
                if (relative.Length == 0)
                {
                    targets.Add(new RescanTarget("", Recursive: false));
                    continue;
                }
                // The parent picks up created, removed and renamed entries (a vanished subfolder is tombstoned there).
                targets.Add(new RescanTarget(LibraryPaths.ParentOf(relative), Recursive: false));
                // A directory that appeared (created or renamed into place) brings its whole content.
                if (ReferenceEquals(path, change.Path) && change.IsDirectory && change.Change is FileSystemChange.Created or FileSystemChange.Renamed)
                {
                    targets.Add(new RescanTarget(relative, Recursive: true));
                }
            }
        }
        return Coalesce(targets);
    }

    /// <summary>Drops targets already covered by a recursive rescan of an ancestor (or of the same directory).</summary>
    internal static IReadOnlyList<RescanTarget> Coalesce(IReadOnlySet<RescanTarget> targets)
    {
        var recursive = targets.Where(t => t.Recursive).Select(t => t.RelativePath).ToList();
        bool Covered(RescanTarget target) => recursive.Any(ancestor =>
            ancestor == target.RelativePath
                ? !target.Recursive
                : ancestor.Length == 0 || target.RelativePath.StartsWith(ancestor + "/", StringComparison.Ordinal));
        return targets.Where(t => !Covered(t)).OrderBy(t => t.RelativePath, StringComparer.Ordinal).ToList();
    }

    internal static bool IsHidden(string relativePath) => relativePath.Split('/').Any(part => part.StartsWith('.'));
}
