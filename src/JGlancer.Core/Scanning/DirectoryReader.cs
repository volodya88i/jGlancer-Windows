namespace JGlancer.Core.Scanning;

/// <summary>One entry of a directory, with the facts stage 1 needs.</summary>
/// <param name="IsHidden">Hidden or system attribute (Windows); a leading dot also counts as hidden everywhere.</param>
/// <param name="IsLink">A symbolic link, junction or other reparse point: never followed.</param>
/// <param name="FileId">File identifier on its volume, when the reader can get it without opening the file.</param>
public sealed record DirectoryEntry(
    string Name,
    bool IsDirectory,
    bool IsHidden,
    bool IsLink,
    long Size,
    DateTimeOffset ModifiedAt,
    DateTimeOffset? CreatedAt,
    ulong? FileId);

/// <summary>Lists one directory (platform layer: the Windows reader also returns NTFS file ids).</summary>
public interface IDirectoryReader
{
    /// <summary>The entries of <paramref name="directory"/>; throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>.</summary>
    IReadOnlyList<DirectoryEntry> Read(string directory);

    /// <summary>The identifier of the volume holding <paramref name="path"/>, or null if unknown.</summary>
    string? VolumeId(string path);

    /// <summary>The canonical form of an existing directory path (links resolved, the case on disk).</summary>
    string Canonical(string path);
}

/// <summary>Portable reader on .NET's enumeration; no file ids (identity then relies on the quick hash).</summary>
public sealed class ManagedDirectoryReader : IDirectoryReader
{
    private static readonly EnumerationOptions Options = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    public IReadOnlyList<DirectoryEntry> Read(string directory)
    {
        var entries = new List<DirectoryEntry>();
        foreach (var info in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", Options))
        {
            var attributes = info.Attributes;
            var isDirectory = attributes.HasFlag(FileAttributes.Directory);
            entries.Add(new DirectoryEntry(
                info.Name,
                isDirectory,
                attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System) || info.Name.StartsWith('.'),
                attributes.HasFlag(FileAttributes.ReparsePoint),
                isDirectory ? 0 : ((FileInfo)info).Length,
                new DateTimeOffset(info.LastWriteTimeUtc),
                new DateTimeOffset(info.CreationTimeUtc),
                FileId: null));
        }
        return entries;
    }

    public string? VolumeId(string path) => null;

    public string Canonical(string path)
    {
        var full = Path.GetFullPath(path);
        return new DirectoryInfo(full).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? full;
    }
}
