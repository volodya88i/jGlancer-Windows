using JGlancer.Core.Model;
using Microsoft.Data.Sqlite;

namespace JGlancer.Core.Store;

/// <summary>The app's own file operations in the index (macOS LibraryStore+FileOps): ids survive moves and the Recycle Bin.</summary>
public sealed partial class LibraryStore
{
    /// <summary>
    /// The file at <paramref name="source"/> now lives at <paramref name="destination"/>: its asset follows (same id,
    /// flags, albums). A destination outside the library tombstones it. Returns the asset id, or null when none was there.
    /// </summary>
    public Task<long?> MoveAssetAsync(string source, string destination, DateTimeOffset? now = null) => WriteAsync(connection =>
    {
        if (PresentAsset(connection, source) is not { } asset)
        {
            return (long?)null;
        }
        if (RootRelative(connection, destination) is not { } target)
        {
            asset.State = (int)AssetState.Missing;
            asset.MissingSince = UnixMilliseconds.From(now ?? DateTimeOffset.UtcNow);
            asset.Update(connection);
            return asset.Id;
        }
        var folder = EnsureFolder(connection, target.RootId, LibraryPaths.ParentOf(target.Relative));
        var name = LibraryPaths.NameOf(target.Relative);
        // A tombstone of a replaced file may hold the name.
        Sql.Execute(connection, "DELETE FROM asset WHERE folder_id = @folder AND name = @name AND state = @missing AND id != @id",
            ("@folder", folder.Id), ("@name", name), ("@missing", (int)AssetState.Missing), ("@id", asset.Id));
        asset.FolderId = folder.Id;
        asset.Name = name;
        asset.NameKey = NaturalSortKey.Make(name);
        asset.Update(connection);
        return asset.Id;
    });

    /// <summary>The file at <paramref name="path"/> went to the Recycle Bin: its asset becomes a tombstone (kept for a restore).</summary>
    public Task MarkMissingAsync(string path, DateTimeOffset? now = null) => WriteAsync(connection =>
    {
        if (PresentAsset(connection, path) is { } asset)
        {
            asset.State = (int)AssetState.Missing;
            asset.MissingSince = UnixMilliseconds.From(now ?? DateTimeOffset.UtcNow);
            asset.Update(connection);
        }
    });

    /// <summary>A file came back at <paramref name="path"/>: the tombstone there becomes present again, same id.</summary>
    public Task<long?> ReviveAsync(string path) => WriteAsync(connection =>
    {
        if (RootRelative(connection, path) is not { } target)
        {
            return (long?)null;
        }
        var folder = Sql.Rows(connection, $"SELECT {FolderRow.Columns} FROM folder WHERE root_id = @root AND rel_path = @path",
            FolderRow.Read, ("@root", target.RootId), ("@path", LibraryPaths.ParentOf(target.Relative))).FirstOrDefault();
        if (folder is null)
        {
            return null;
        }
        var asset = Sql.Rows(connection, $"SELECT {AssetRow.Columns} FROM asset WHERE folder_id = @folder AND name = @name ORDER BY state",
            AssetRow.Read, ("@folder", folder.Id), ("@name", LibraryPaths.NameOf(target.Relative))).FirstOrDefault();
        if (asset is null)
        {
            return null;
        }
        asset.State = (int)AssetState.Present;
        asset.MissingSince = null;
        asset.Update(connection);
        if (folder.State != (int)AssetState.Present)
        {
            EnsureFolder(connection, target.RootId, folder.RelPath);
        }
        return asset.Id;
    });

    private static AssetRow? PresentAsset(SqliteConnection connection, string path)
    {
        if (RootRelative(connection, path) is not { } target)
        {
            return null;
        }
        return Sql.Rows(connection, $"""
            SELECT {AssetRow.Columns} FROM asset JOIN folder ON folder.id = asset.folder_id
            WHERE folder.root_id = @root AND folder.rel_path = @folder AND asset.name = @name AND asset.state = 0
            """, AssetRow.Read, ("@root", target.RootId), ("@folder", LibraryPaths.ParentOf(target.Relative)),
            ("@name", LibraryPaths.NameOf(target.Relative))).FirstOrDefault();
    }

    /// <summary>The root holding the file <paramref name="path"/> and the file's path relative to it.</summary>
    private static (long RootId, string Relative)? RootRelative(SqliteConnection connection, string path)
    {
        foreach (var root in Sql.Rows(connection, $"SELECT {RootRow.Columns} FROM root_folder", RootRow.Read))
        {
            if (LibraryPaths.Relative(path, root.Path) is { Length: > 0 } relative)
            {
                return (root.Id, relative);
            }
        }
        return null;
    }
}
