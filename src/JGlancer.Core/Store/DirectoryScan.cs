using JGlancer.Core.Model;

namespace JGlancer.Core.Store;

/// <summary>One media file found in a directory listing (stage 1 facts only).</summary>
/// <param name="FileId">File identifier on its volume (NTFS file id; inode elsewhere).</param>
public sealed record ScannedFile(
    string Name,
    MediaKind Kind,
    string? TypeIdentifier,
    long Size,
    DateTimeOffset ModifiedAt,
    DateTimeOffset? CreatedAt,
    ulong? FileId);

/// <summary>A complete, shallow listing of one directory inside a root, ready to reconcile with the database.</summary>
/// <param name="RelativePath">Path relative to the root, <c>/</c>-separated, empty for the root itself.</param>
/// <param name="Subdirectories">Names of the visible subdirectories.</param>
public sealed record DirectoryScan(
    long RootId,
    string RelativePath,
    IReadOnlyList<ScannedFile> Files,
    IReadOnlyList<string> Subdirectories,
    DateTimeOffset ScannedAt);

/// <summary>What reconciling one directory changed.</summary>
public sealed class DirectoryScanResult(long folderId)
{
    public long FolderId { get; } = folderId;

    /// <summary>New assets.</summary>
    public int Added { get; set; }

    /// <summary>Assets whose content changed (size or modification date); their metadata is re-extracted.</summary>
    public int Updated { get; set; }

    /// <summary>Assets recognized as moved or renamed from elsewhere; they keep their id.</summary>
    public int Moved { get; set; }

    /// <summary>Tombstoned assets whose file is back at the same path.</summary>
    public int Revived { get; set; }

    /// <summary>
    /// Tombstones created (including in removed subfolders). A file moved elsewhere is first tombstoned in its old
    /// folder and then counted in <see cref="Moved"/> where it reappears.
    /// </summary>
    public int Missing { get; set; }

    public int Unchanged { get; set; }

    /// <summary>Subfolders that vanished.</summary>
    public int RemovedSubfolders { get; set; }

    /// <summary>Whether anything in the database changed.</summary>
    public bool HasChanges => Added + Updated + Moved + Revived + Missing + RemovedSubfolders > 0;
}

/// <summary>File-system questions the reconciler asks while matching identities (injected: the store does no IO).</summary>
public interface IFileProbe
{
    /// <summary>Whether a file exists at <paramref name="path"/>.</summary>
    bool FileExists(string path);

    /// <summary>The quick content hash of the file at <paramref name="path"/>, or null if unreadable.</summary>
    byte[]? QuickHash(string path);
}
