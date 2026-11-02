using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Security.Principal;
using JGlancer.Core.FileOps;
using JGlancer.Core.Scanning;

namespace JGlancer.Platform.Windows;

/// <summary>
/// The real file system for file operations (macOS LocalFileSystem) with the Recycle Bin as the Trash. Windows does
/// not say where a file went in the Recycle Bin, so after sending it there the item is found by its <c>$I</c> record
/// (original path, deletion time) in <c>$Recycle.Bin\&lt;user SID&gt;</c> of its drive; restoring moves the
/// <c>$R</c> item back and removes the record, so the Recycle Bin no longer lists it (W-47).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFileSystem : IFileSystem
{
    private readonly WindowsDirectoryReader _reader = new();

    public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    public string? Volume(string path)
    {
        var probe = Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
        return _reader.VolumeId(probe) ?? Path.GetPathRoot(probe)?.ToUpperInvariant();
    }

    /// <summary>How long a file another program (a player that just stopped) still holds open is waited for.</summary>
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(1.5);

    public void Move(string source, string destination) => WhileLocked(() =>
    {
        if (Directory.Exists(source))
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination, overwrite: false);
        }
    });

    /// <summary>Runs <paramref name="change"/>, again for a moment while the file is in use (sharing violation).</summary>
    private static void WhileLocked(Action change)
    {
        var started = DateTime.UtcNow;
        while (true)
        {
            try
            {
                change();
                return;
            }
            catch (IOException error) when ((error.HResult & 0xFFFF) is 32 or 33 && DateTime.UtcNow - started < LockWait)
            {
                Thread.Sleep(100);
            }
        }
    }

    public void Copy(string source, string destination) => File.Copy(source, destination, overwrite: false);

    public void Remove(string path) => File.Delete(path);

    public string Trash(string path)
    {
        var full = Path.GetFullPath(path);
        var sent = DateTime.UtcNow.AddSeconds(-2);
        // A file a player just let go of may stay open a moment.
        var started = DateTime.UtcNow;
        while (!RecycleBin.Send(full, out var error) && error is 32 or 33 && DateTime.UtcNow - started < LockWait)
        {
            Thread.Sleep(100);
        }
        if (Exists(full))
        {
            throw new FileOperationException(FileOperationError.NotInRecycleBin);
        }
        // Not found: Windows deleted it for good after asking (a drive without a Recycle Bin); no restore then.
        return Locate(full, sent) ?? "";
    }

    public void Restore(string trashed, string destination)
    {
        if (trashed.Length == 0 || !Exists(trashed))
        {
            throw new FileOperationException(FileOperationError.NotInRecycleBin);
        }
        Move(trashed, destination);
        // The record without its item would stay listed in the Recycle Bin as a broken entry.
        var record = Record(trashed);
        if (File.Exists(record))
        {
            try
            {
                File.Delete(record);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public void CreateDirectory(string path)
    {
        if (Exists(path))
        {
            throw new FileOperationException(FileOperationError.NameTaken);
        }
        Directory.CreateDirectory(path);
    }

    public long? Size(string path) => File.Exists(path) ? new FileInfo(path).Length : null;

    public byte[]? QuickHash(string path) => Core.Scanning.QuickHash.Compute(path);

    // MARK: Recycle Bin

    /// <summary>The user's Recycle Bin folder on the drive of <paramref name="path"/>.</summary>
    internal static string? BinFolder(string path)
    {
        var root = Path.GetPathRoot(path);
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        return root is null || sid is null ? null : Path.Combine(root, "$Recycle.Bin", sid);
    }

    /// <summary>The <c>$R</c> item of <paramref name="original"/> deleted at or after <paramref name="since"/> (the latest), or null.</summary>
    internal static string? Locate(string original, DateTime since)
    {
        if (BinFolder(original) is not { } bin || !Directory.Exists(bin))
        {
            return null;
        }
        string? found = null;
        var latest = DateTime.MinValue;
        foreach (var record in Directory.EnumerateFiles(bin, "$I*"))
        {
            if (ReadRecord(record) is not { } entry || entry.Deleted < since || entry.Deleted < latest
                || !string.Equals(entry.Path, original, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var item = Path.Combine(bin, "$R" + Path.GetFileName(record)[2..]);
            if (File.Exists(item) || Directory.Exists(item))
            {
                found = item;
                latest = entry.Deleted;
            }
        }
        return found;
    }

    /// <summary>The <c>$I</c> record of a <c>$R</c> item.</summary>
    private static string Record(string item) => Path.Combine(Path.GetDirectoryName(item) ?? "", "$I" + Path.GetFileName(item)[2..]);

    /// <summary>
    /// A <c>$I</c> record: version (8 bytes), size (8), deletion time (8, FILETIME), then the original path —
    /// version 1 a fixed 260-character field, version 2 a 4-byte length in characters and the path.
    /// </summary>
    internal static (string Path, DateTime Deleted)? ReadRecord(string record)
    {
        try
        {
            var bytes = File.ReadAllBytes(record);
            if (bytes.Length < 24)
            {
                return null;
            }
            var version = BinaryPrimitives.ReadInt64LittleEndian(bytes);
            var deleted = DateTime.FromFileTimeUtc(BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(16)));
            string path;
            if (version == 1 && bytes.Length >= 24 + 520)
            {
                path = System.Text.Encoding.Unicode.GetString(bytes, 24, 520);
            }
            else if (version == 2 && bytes.Length >= 28)
            {
                var characters = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24));
                path = System.Text.Encoding.Unicode.GetString(bytes, 28, Math.Min(characters * 2, bytes.Length - 28));
            }
            else
            {
                return null;
            }
            return (path.TrimEnd('\0').Split('\0')[0], deleted);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
