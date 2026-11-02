using JGlancer.Core.Model;
using Microsoft.Data.Sqlite;

namespace JGlancer.Core.Store;

public sealed partial class LibraryStore
{
    /// <summary>
    /// Adds a library root and its top folder row. Never touches files. <paramref name="path"/> is absolute and
    /// canonical. Throws <see cref="RootAlreadyExistsException"/> or <see cref="RootOverlapsException"/>.
    /// </summary>
    public Task<RootFolder> AddRootAsync(string path, string? volumeId = null, bool isRecursive = true, bool isWatched = true, DateTimeOffset? now = null)
    {
        var normalized = LibraryPaths.Normalize(path);
        var addedAt = UnixMilliseconds.From(now ?? DateTimeOffset.UtcNow);
        return WriteAsync(connection =>
        {
            foreach (var existing in Sql.Rows(connection, "SELECT path FROM root_folder", r => r.GetString(0)))
            {
                if (string.Equals(existing, normalized, LibraryPaths.Comparison))
                {
                    throw new RootAlreadyExistsException(normalized);
                }
                if (LibraryPaths.IsSameOrInside(normalized, existing) || LibraryPaths.IsSameOrInside(existing, normalized))
                {
                    throw new RootOverlapsException(normalized, existing);
                }
            }
            var id = Sql.Long(connection, """
                INSERT INTO root_folder (path, volume_uuid, is_recursive, is_watched, added_at)
                VALUES (@path, @volume, @recursive, @watched, @added) RETURNING id
                """, ("@path", normalized), ("@volume", volumeId), ("@recursive", isRecursive), ("@watched", isWatched), ("@added", addedAt)) ?? 0;
            Sql.Execute(connection, "INSERT INTO folder (root_id, parent_id, rel_path, name, state) VALUES (@root, NULL, '', @name, 0)",
                ("@root", id), ("@name", LibraryPaths.RootName(normalized)));
            return new RootFolder(id, normalized, volumeId, isRecursive, isWatched, UnixMilliseconds.ToDate(addedAt));
        });
    }

    /// <summary>All roots, in the order they were added.</summary>
    public Task<IReadOnlyList<RootFolder>> RootsAsync() => ReadAsync<IReadOnlyList<RootFolder>>(connection =>
        Sql.Rows(connection, $"SELECT {RootRow.Columns} FROM root_folder ORDER BY id", r => RootRow.Read(r).ToModel()));

    /// <summary>One root; throws <see cref="RootNotFoundException"/>.</summary>
    public Task<RootFolder> RootAsync(long id) => ReadAsync(connection => RootRecord(connection, id).ToModel());

    /// <summary>Removes a root with all its folder and asset rows. Files on disk are untouched.</summary>
    public Task RemoveRootAsync(long id) => WriteAsync(connection =>
    {
        Sql.Execute(connection, "DELETE FROM root_folder WHERE id = @id", ("@id", id));
    });

    internal static RootRow RootRecord(SqliteConnection connection, long id) =>
        Sql.Rows(connection, $"SELECT {RootRow.Columns} FROM root_folder WHERE id = @id", RootRow.Read, ("@id", id)).FirstOrDefault()
        ?? throw new RootNotFoundException(id);
}
