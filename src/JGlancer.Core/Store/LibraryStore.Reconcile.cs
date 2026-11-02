using JGlancer.Core.Model;
using Microsoft.Data.Sqlite;

namespace JGlancer.Core.Store;

public sealed partial class LibraryStore
{
    /// <summary>
    /// Reconciles one directory listing with the database in a single transaction: adds, updates, tombstones,
    /// revives, and recognizes external moves (macOS ARCHITECTURE §5, §13).
    /// </summary>
    public Task<DirectoryScanResult> ApplyDirectoryScanAsync(DirectoryScan scan, IFileProbe probe) =>
        WriteAsync(connection => Reconcile(connection, scan, probe));

    /// <summary>Tombstones a directory and everything below it; returns the number of assets tombstoned.</summary>
    public Task<int> MarkFolderMissingAsync(long rootId, string relativePath, DateTimeOffset? now = null) =>
        WriteAsync(connection => MarkSubtreeMissing(connection, rootId, relativePath, UnixMilliseconds.From(now ?? DateTimeOffset.UtcNow)));

    internal static DirectoryScanResult Reconcile(SqliteConnection connection, DirectoryScan scan, IFileProbe probe)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(probe);
        var root = RootRecord(connection, scan.RootId);
        var now = UnixMilliseconds.From(scan.ScannedAt);
        var folderId = EnsureFolder(connection, scan.RootId, scan.RelativePath).Id;
        var result = new DirectoryScanResult(folderId);
        var existing = Sql.Rows(connection, $"SELECT {AssetRow.Columns} FROM asset WHERE folder_id = @folder", AssetRow.Read, ("@folder", folderId));
        // Vanished files become tombstones first, so a rename inside this folder can match them below.
        TombstoneVanished(connection, existing, scan, now, result);
        var newFiles = RefreshKnown(connection, existing, scan, now, result);
        AddOrMatch(connection, newFiles, folderId, LibraryPaths.Absolute(root.Path, scan.RelativePath), root.VolumeId, now, probe, result);
        TombstoneRemovedSubfolders(connection, scan, folderId, now, result);
        return result;
    }

    private static void TombstoneVanished(SqliteConnection connection, List<AssetRow> existing, DirectoryScan scan, long now, DirectoryScanResult result)
    {
        var scannedNames = scan.Files.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var row in existing.Where(r => r.State == (int)AssetState.Present && !scannedNames.Contains(r.Name)))
        {
            row.State = (int)AssetState.Missing;
            row.MissingSince = now;
            row.Update(connection);
            result.Missing++;
        }
    }

    /// <summary>Updates or revives rows whose name is already known; returns the files with new names.</summary>
    private static List<ScannedFile> RefreshKnown(SqliteConnection connection, List<AssetRow> existing, DirectoryScan scan, long now, DirectoryScanResult result)
    {
        var byName = new Dictionary<string, AssetRow>(StringComparer.Ordinal);
        foreach (var row in existing)
        {
            byName.TryAdd(row.Name, row);
        }
        var newFiles = new List<ScannedFile>();
        foreach (var file in scan.Files)
        {
            if (!byName.TryGetValue(file.Name, out var row))
            {
                newFiles.Add(file);
                continue;
            }
            var sameContent = row.Size == file.Size && row.Mtime == UnixMilliseconds.From(file.ModifiedAt);
            var wasMissing = row.State == (int)AssetState.Missing;
            if (sameContent && !wasMissing)
            {
                var fileId = ToStored(file.FileId);
                if (row.Inode != fileId)
                {
                    row.Inode = fileId;
                    row.Update(connection);
                }
                result.Unchanged++;
                continue;
            }
            if (!sameContent)
            {
                row.ResetMetadata();
            }
            Apply(file, row, now);
            row.Update(connection);
            if (wasMissing)
            {
                result.Revived++;
            }
            else
            {
                result.Updated++;
            }
        }
        return newFiles;
    }

    /// <summary>New names: an external move or rename of a known asset (keeps its id), or a new asset.</summary>
    private static void AddOrMatch(
        SqliteConnection connection, List<ScannedFile> newFiles, long folderId, string folderPath, string? volumeId, long now,
        IFileProbe probe, DirectoryScanResult result)
    {
        if (newFiles.Count == 0)
        {
            return;
        }
        var candidates = IdentityCandidates(connection, newFiles, volumeId);
        var claimed = new HashSet<long>();
        foreach (var file in newFiles)
        {
            var filePath = LibraryPaths.Combine(folderPath, file.Name);
            var facts = new IdentityMatcher.NewFile(file.Size, UnixMilliseconds.From(file.ModifiedAt), file.FileId);
            var matchId = IdentityMatcher.Match(facts, candidates, claimed, probe, () => probe.QuickHash(filePath));
            var matched = matchId is { } id
                ? Sql.Rows(connection, $"SELECT {AssetRow.Columns} FROM asset WHERE id = @id", AssetRow.Read, ("@id", id)).FirstOrDefault()
                : null;
            if (matched is not null)
            {
                matched.FolderId = folderId;
                matched.Name = file.Name;
                matched.NameKey = NaturalSortKey.Make(file.Name);
                Apply(file, matched, now);
                matched.Update(connection);
                claimed.Add(matched.Id);
                result.Moved++;
            }
            else
            {
                var row = new AssetRow
                {
                    FolderId = folderId,
                    Name = file.Name,
                    NameKey = NaturalSortKey.Make(file.Name),
                    State = (int)AssetState.Present,
                    MetaState = (int)MetadataState.Pending,
                };
                Apply(file, row, now);
                row.Insert(connection);
                result.Added++;
            }
        }
    }

    private static void TombstoneRemovedSubfolders(SqliteConnection connection, DirectoryScan scan, long folderId, long now, DirectoryScanResult result)
    {
        var present = scan.Subdirectories.ToHashSet(StringComparer.Ordinal);
        var children = Sql.Rows(connection, $"SELECT {FolderRow.Columns} FROM folder WHERE parent_id = @parent AND state = 0",
            FolderRow.Read, ("@parent", folderId));
        foreach (var child in children.Where(c => !present.Contains(c.Name)))
        {
            result.Missing += MarkSubtreeMissing(connection, scan.RootId, child.RelPath, now);
            result.RemovedSubfolders++;
        }
    }

    /// <summary>Copies stage-1 facts onto a row and marks it present.</summary>
    private static void Apply(ScannedFile file, AssetRow row, long now)
    {
        row.Kind = (int)file.Kind;
        row.Uti = file.TypeIdentifier;
        row.Size = file.Size;
        row.Mtime = UnixMilliseconds.From(file.ModifiedAt);
        row.BirthTime = file.CreatedAt is { } created ? UnixMilliseconds.From(created) : null;
        row.Inode = ToStored(file.FileId);
        row.State = (int)AssetState.Present;
        row.MissingSince = null;
        row.IndexedAt = now;
    }

    /// <summary>Existing assets that could be one of <paramref name="files"/>: same file id, or a tombstone of the same size.</summary>
    private static List<IdentityCandidate> IdentityCandidates(SqliteConnection connection, List<ScannedFile> files, string? volumeId)
    {
        const string Select = """
            SELECT asset.id, asset.inode, asset.size, asset.mtime, asset.quick_hash, asset.state, asset.name,
                   folder.rel_path, root_folder.path, root_folder.volume_uuid
            FROM asset JOIN folder ON folder.id = asset.folder_id JOIN root_folder ON root_folder.id = folder.root_id
            """;
        var fileIds = files.Where(f => f.FileId is not null).Select(f => ToStored(f.FileId)!.Value).Distinct().ToList();
        var sizes = files.Select(f => f.Size).Distinct().ToList();
        var candidates = new List<IdentityCandidate>();
        var seen = new HashSet<long>();
        IdentityCandidate Map(SqliteDataReader r)
        {
            var candidateVolume = r.IsDBNull(9) ? null : r.GetString(9);
            return new IdentityCandidate(
                r.GetInt64(0),
                r.IsDBNull(1) ? null : unchecked((ulong)r.GetInt64(1)),
                r.GetInt64(2),
                r.GetInt64(3),
                r.IsDBNull(4) ? null : (byte[])r.GetValue(4),
                r.GetInt32(5) == (int)AssetState.Missing,
                volumeId is null || candidateVolume is null || string.Equals(volumeId, candidateVolume, StringComparison.OrdinalIgnoreCase),
                LibraryPaths.Combine(LibraryPaths.Absolute(r.GetString(8), r.GetString(7)), r.GetString(6)));
        }
        void Collect(string condition, IReadOnlyList<long> values)
        {
            foreach (var chunk in Sql.Chunks(values))
            {
                var (list, parameters) = Sql.InList(chunk);
                foreach (var candidate in Sql.Rows(connection, $"{Select} WHERE {condition} IN ({list})", Map, parameters))
                {
                    if (seen.Add(candidate.Id))
                    {
                        candidates.Add(candidate);
                    }
                }
            }
        }
        Collect("asset.inode", fileIds);
        Collect("asset.state = 1 AND asset.quick_hash IS NOT NULL AND asset.size", sizes);
        return candidates;
    }

    /// <summary>The folder row for <paramref name="relativePath"/>, created (with its parents) or revived as needed.</summary>
    internal static FolderRow EnsureFolder(SqliteConnection connection, long rootId, string relativePath)
    {
        var existing = Sql.Rows(connection, $"SELECT {FolderRow.Columns} FROM folder WHERE root_id = @root AND rel_path = @path",
            FolderRow.Read, ("@root", rootId), ("@path", relativePath)).FirstOrDefault();
        if (existing is not null)
        {
            if (existing.State != (int)AssetState.Present)
            {
                Sql.Execute(connection, "UPDATE folder SET state = 0 WHERE id = @id", ("@id", existing.Id));
                existing = existing with { State = (int)AssetState.Present };
            }
            return existing;
        }
        var root = RootRecord(connection, rootId);
        long? parentId = null;
        var name = LibraryPaths.RootName(root.Path);
        if (relativePath.Length > 0)
        {
            parentId = EnsureFolder(connection, rootId, LibraryPaths.ParentOf(relativePath)).Id;
            name = LibraryPaths.NameOf(relativePath);
        }
        var id = Sql.Long(connection, """
            INSERT INTO folder (root_id, parent_id, rel_path, name, state) VALUES (@root, @parent, @path, @name, 0) RETURNING id
            """, ("@root", rootId), ("@parent", parentId), ("@path", relativePath), ("@name", name)) ?? 0;
        return new FolderRow(id, rootId, parentId, relativePath, name, (int)AssetState.Present);
    }

    /// <summary>Tombstones a folder subtree and its present assets; returns the number of assets tombstoned.</summary>
    internal static int MarkSubtreeMissing(SqliteConnection connection, long rootId, string relativePath, long now)
    {
        const string Subtree = """
            SELECT id FROM folder WHERE root_id = @root
              AND (rel_path = @path OR (rel_path >= @path || '/' AND rel_path < @path || '0'))
            """;
        var tombstoned = Sql.Execute(connection, $"UPDATE asset SET state = 1, missing_since = @now WHERE state = 0 AND folder_id IN ({Subtree})",
            ("@root", rootId), ("@path", relativePath), ("@now", now));
        Sql.Execute(connection, $"UPDATE folder SET state = 1 WHERE id IN ({Subtree})", ("@root", rootId), ("@path", relativePath));
        return tombstoned;
    }

    /// <summary>File ids are unsigned 64-bit; SQLite stores signed integers, so the bits are kept as they are.</summary>
    private static long? ToStored(ulong? fileId) => fileId is { } id ? unchecked((long)id) : null;
}
