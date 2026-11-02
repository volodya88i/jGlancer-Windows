using System.Globalization;
using Microsoft.Data.Sqlite;

namespace JGlancer.Core.Store;

/// <summary>What happened when the library database could not be used as it was (macOS D-66).</summary>
public abstract record LibraryRecovery
{
    private LibraryRecovery()
    {
    }

    /// <summary>The damaged file was set aside and the last backup (made at <paramref name="BackupDate"/>) put in its place.</summary>
    public sealed record RestoredFromBackup(DateTimeOffset BackupDate, string DamagedCopy) : LibraryRecovery;

    /// <summary>No usable backup: a new, empty library; the damaged file was set aside.</summary>
    public sealed record StartedEmpty(string DamagedCopy) : LibraryRecovery;
}

/// <summary>
/// Opening with a safety net, and the daily backup. Nothing is ever deleted: a damaged database is renamed
/// <c>library-damaged-&lt;date&gt;.sqlite</c> next to it, so its albums and flags can still be rescued by hand.
/// </summary>
public sealed partial class LibraryStore
{
    /// <summary>
    /// Opens <paramref name="path"/>; if it is not a database or fails its integrity check, sets it aside and opens the
    /// backup at <paramref name="backupPath"/> instead (or a new library when there is none that works).
    /// </summary>
    public static (LibraryStore Store, LibraryRecovery? Recovery) OpenWithRecovery(string path, string backupPath, DateTimeOffset? now = null)
    {
        var time = now ?? DateTimeOffset.Now;
        if (TryOpenIntact(path) is { } healthy)
        {
            return (healthy, null);
        }
        var damaged = SetAside(path, time);
        if (File.Exists(backupPath))
        {
            var backupDate = new DateTimeOffset(File.GetLastWriteTimeUtc(backupPath));
            File.Copy(backupPath, path);
            if (TryOpenIntact(path) is { } restored)
            {
                return (restored, new LibraryRecovery.RestoredFromBackup(backupDate, damaged));
            }
            // The backup is no good either: keep it beside the damaged one and start over.
            SetAside(path, time.AddSeconds(1));
        }
        return (Open(path), new LibraryRecovery.StartedEmpty(damaged));
    }

    /// <summary><c>PRAGMA quick_check</c> says "ok".</summary>
    public bool IsIntact() => Read(connection => (Sql.Scalar(connection, "PRAGMA quick_check") as string) == "ok");

    /// <summary>
    /// Writes a consistent copy of the library to <paramref name="backupPath"/> (via a temporary file, then a swap) when
    /// the last backup is older than <paramref name="maxAge"/> or missing. Returns true when a new backup was made.
    /// </summary>
    public Task<bool> BackupAsync(string backupPath, TimeSpan maxAge, DateTimeOffset? now = null) => Task.Run(() =>
    {
        var time = now ?? DateTimeOffset.UtcNow;
        if (File.Exists(backupPath) && time - new DateTimeOffset(File.GetLastWriteTimeUtc(backupPath)) < maxAge)
        {
            return false;
        }
        var directory = Path.GetDirectoryName(Path.GetFullPath(backupPath))!;
        var temporary = Path.Combine(directory, $".{Path.GetFileName(backupPath)}.{Guid.NewGuid():N}");
        using (var connection = Connect())
        {
            Sql.Execute(connection, "VACUUM INTO @target", ("@target", temporary));
        }
        if (File.Exists(backupPath))
        {
            File.Replace(temporary, backupPath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temporary, backupPath);
        }
        File.SetLastWriteTimeUtc(backupPath, time.UtcDateTime);
        return true;
    });

    private static LibraryStore? TryOpenIntact(string path)
    {
        LibraryStore? store = null;
        try
        {
            store = Open(path);
            if (store.IsIntact())
            {
                return store;
            }
        }
        catch (SqliteException)
        {
        }
        store?.Dispose();
        return null;
    }

    /// <summary>Renames the database and its WAL files to <c>library-damaged-…</c>; returns the new database path.</summary>
    private static string SetAside(string path, DateTimeOffset now)
    {
        ClearPool(ConnectionString(path));
        var stamp = now.ToString("yyyy-MM-ddTHH-mm-ss", CultureInfo.InvariantCulture);
        var target = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,
            $"{Path.GetFileNameWithoutExtension(path)}-damaged-{stamp}{Path.GetExtension(path)}");
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            if (File.Exists(path + suffix))
            {
                File.Move(path + suffix, target + suffix);
            }
        }
        return target;
    }
}
