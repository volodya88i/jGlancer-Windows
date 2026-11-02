using JGlancer.Core.Model;
using JGlancer.Core.Store;

namespace JGlancer.Core.Scanning;

/// <summary>
/// Lists one directory for stage 1: media files with their file-system facts, and visible subdirectories. Hidden
/// items, links and non-media files are skipped.
/// </summary>
public sealed class DirectoryLister(IDirectoryReader reader)
{
    public IDirectoryReader Reader { get; } = reader;

    /// <summary>Lists <paramref name="directory"/>, a folder inside root <paramref name="rootId"/> at <paramref name="relativePath"/>.</summary>
    public DirectoryScan List(string directory, long rootId, string relativePath, DateTimeOffset? now = null)
    {
        var files = new List<ScannedFile>();
        var subdirectories = new List<string>();
        foreach (var entry in Reader.Read(directory))
        {
            if (entry.IsHidden || entry.IsLink)
            {
                continue;
            }
            if (entry.IsDirectory)
            {
                subdirectories.Add(entry.Name);
                continue;
            }
            if (MediaClassifier.Classify(entry.Name) is not { } classification)
            {
                continue;
            }
            files.Add(new ScannedFile(entry.Name, classification.Kind, classification.TypeIdentifier, entry.Size,
                entry.ModifiedAt, entry.CreatedAt, entry.FileId));
        }
        return new DirectoryScan(rootId, relativePath, files, subdirectories, now ?? DateTimeOffset.UtcNow);
    }
}

/// <summary>
/// Media files directly inside one folder, for viewing a file opened from Explorer (siblings listed shallowly even
/// when the folder is not in the library; macOS FEATURES §13).
/// </summary>
public static class FolderListing
{
    /// <summary>Files of <paramref name="kinds"/> in <paramref name="folder"/>, in natural name order.</summary>
    public static IReadOnlyList<ScannedFile> Files(string folder, IReadOnlySet<MediaKind> kinds, DirectoryLister lister)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(lister);
        return lister.List(folder, rootId: 0, relativePath: "").Files
            .Where(f => kinds.Contains(f.Kind))
            .OrderBy(f => NaturalSortKey.Make(f.Name), StringComparer.Ordinal)
            .ToList();
    }
}
