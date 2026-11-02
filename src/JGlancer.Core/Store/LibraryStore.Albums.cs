using JGlancer.Core.Model;
using Microsoft.Data.Sqlite;

namespace JGlancer.Core.Store;

/// <summary>
/// Virtual albums (macOS LibraryStore+Albums, FEATURES §6, ARCHITECTURE §13): lists of asset ids, so items follow the
/// app's moves and renames. Deleting an album or removing items never touches files. Items whose files vanished stay
/// (hidden) until <see cref="PurgeAlbumPlaceholdersAsync"/> drops the old ones, so a file that comes back is in its
/// albums again.
/// </summary>
public sealed partial class LibraryStore
{
    /// <summary>How long a vanished file stays in its albums.</summary>
    public static readonly TimeSpan AlbumPlaceholderLifetime = TimeSpan.FromDays(30);

    public Task<Album> CreateAlbumAsync(string name, DateTimeOffset? now = null) => WriteAsync(connection =>
    {
        var created = now ?? DateTimeOffset.UtcNow;
        var order = Sql.Long(connection, "SELECT COALESCE(MAX(sort_order), -1) + 1 FROM album") ?? 0;
        var id = Sql.Long(connection, "INSERT INTO album (name, created_at, sort_order) VALUES (@name, @created, @order) RETURNING id",
            ("@name", name), ("@created", UnixMilliseconds.From(created)), ("@order", order)) ?? 0;
        return new Album(id, name, created, null, 0);
    });

    public Task RenameAlbumAsync(long id, string name) => WriteAsync(connection =>
        Sql.Execute(connection, "UPDATE album SET name = @name WHERE id = @id", ("@name", name), ("@id", id)));

    /// <summary>Deletes the album and its item list; the files are not touched.</summary>
    public Task DeleteAlbumAsync(long id) => WriteAsync(connection =>
    {
        Sql.Execute(connection, "DELETE FROM album_item WHERE album_id = @id", ("@id", id));
        Sql.Execute(connection, "DELETE FROM album WHERE id = @id", ("@id", id));
    });

    /// <summary>Every album in sidebar order.</summary>
    public Task<IReadOnlyList<Album>> AlbumsAsync() => ReadAsync(FetchAlbums);

    /// <summary>Emits the albums now and after every change (names, items, covers).</summary>
    public IAsyncEnumerable<IReadOnlyList<Album>> ObserveAlbums(CancellationToken cancellation = default) =>
        Observe(FetchAlbums, (a, b) => a.SequenceEqual(b), cancellation);

    /// <summary>The albums each of <paramref name="assetIds"/> belongs to (menus mark "already added").</summary>
    public Task<IReadOnlyDictionary<long, IReadOnlySet<long>>> AlbumMembershipsAsync(IReadOnlyList<long> assetIds) => ReadAsync(connection =>
    {
        var memberships = new Dictionary<long, IReadOnlySet<long>>();
        var wanted = assetIds.ToHashSet();
        foreach (var (asset, album) in Sql.Rows(connection, "SELECT asset_id, album_id FROM album_item", r => (r.GetInt64(0), r.GetInt64(1))))
        {
            if (wanted.Contains(asset))
            {
                var set = memberships.TryGetValue(asset, out var existing) ? (HashSet<long>)existing : [];
                set.Add(album);
                memberships[asset] = set;
            }
        }
        return (IReadOnlyDictionary<long, IReadOnlySet<long>>)memberships;
    });

    /// <summary>Appends assets not yet in the album, in the given order; returns the ones added.</summary>
    public Task<IReadOnlyList<long>> AddToAlbumAsync(long albumId, IReadOnlyList<long> assetIds, DateTimeOffset? now = null) => WriteAsync(connection =>
    {
        var existing = Sql.Rows(connection, "SELECT asset_id FROM album_item WHERE album_id = @album", r => r.GetInt64(0), ("@album", albumId)).ToHashSet();
        var next = Sql.Long(connection, "SELECT COALESCE(MAX(position), -1) + 1 FROM album_item WHERE album_id = @album", ("@album", albumId)) ?? 0;
        var added = new List<long>();
        foreach (var id in assetIds)
        {
            if (existing.Contains(id) || added.Contains(id))
            {
                continue;
            }
            Sql.Execute(connection, """
                INSERT INTO album_item (album_id, asset_id, position, added_at)
                SELECT @album, @asset, @position, @added WHERE EXISTS (SELECT 1 FROM asset WHERE id = @asset)
                """, ("@album", albumId), ("@asset", id), ("@position", next), ("@added", UnixMilliseconds.From(now ?? DateTimeOffset.UtcNow)));
            next++;
            added.Add(id);
        }
        return (IReadOnlyList<long>)added;
    });

    /// <summary>Removes items; returns where they were, for an undo.</summary>
    public Task<IReadOnlyList<AlbumItemPosition>> RemoveFromAlbumAsync(long albumId, IReadOnlyList<long> assetIds) => WriteAsync(connection =>
    {
        var removed = new List<AlbumItemPosition>();
        foreach (var id in assetIds)
        {
            if (Sql.Long(connection, "SELECT position FROM album_item WHERE album_id = @album AND asset_id = @asset",
                ("@album", albumId), ("@asset", id)) is { } position)
            {
                Sql.Execute(connection, "DELETE FROM album_item WHERE album_id = @album AND asset_id = @asset", ("@album", albumId), ("@asset", id));
                removed.Add(new AlbumItemPosition(id, position));
            }
        }
        return (IReadOnlyList<AlbumItemPosition>)removed;
    });

    /// <summary>Puts removed items back where they were (undo of <see cref="RemoveFromAlbumAsync"/>).</summary>
    public Task RestoreToAlbumAsync(long albumId, IReadOnlyList<AlbumItemPosition> items, DateTimeOffset? now = null) => WriteAsync(connection =>
    {
        foreach (var item in items)
        {
            Sql.Execute(connection, """
                INSERT OR IGNORE INTO album_item (album_id, asset_id, position, added_at)
                SELECT @album, @asset, @position, @added
                WHERE EXISTS (SELECT 1 FROM asset WHERE id = @asset) AND EXISTS (SELECT 1 FROM album WHERE id = @album)
                """, ("@album", albumId), ("@asset", item.AssetId), ("@position", item.Position),
                ("@added", UnixMilliseconds.From(now ?? DateTimeOffset.UtcNow)));
        }
    });

    /// <summary>The album's item ids in order (vanished files included).</summary>
    public Task<IReadOnlyList<long>> AlbumOrderAsync(long albumId) => ReadAsync(connection => (IReadOnlyList<long>)AlbumOrder(connection, albumId));

    /// <summary>Moves <paramref name="assetIds"/> (keeping their relative order) in front of <paramref name="before"/>, or to the end when null.</summary>
    public Task MoveInAlbumAsync(long albumId, IReadOnlyList<long> assetIds, long? before) => WriteAsync(connection =>
    {
        var order = AlbumOrder(connection, albumId);
        var moving = order.Where(assetIds.Contains).ToList();
        var rest = order.Where(id => !assetIds.Contains(id)).ToList();
        var index = before is { } target && rest.IndexOf(target) is >= 0 and var at ? at : rest.Count;
        rest.InsertRange(index, moving);
        Renumber(connection, albumId, rest);
    });

    /// <summary>Renumbers the album to <paramref name="order"/> (ids not in the album are ignored; missing ones keep their place at the end).</summary>
    public Task SetAlbumOrderAsync(long albumId, IReadOnlyList<long> order) => WriteAsync(connection => Renumber(connection, albumId, order));

    /// <summary>The album's cover; null goes back to the first item.</summary>
    public Task SetAlbumCoverAsync(long albumId, long? assetId) => WriteAsync(connection =>
        Sql.Execute(connection, "UPDATE album SET cover_asset_id = @asset WHERE id = @id", ("@asset", assetId), ("@id", albumId)));

    /// <summary>Drops album items whose files vanished more than <see cref="AlbumPlaceholderLifetime"/> ago; returns how many.</summary>
    public Task<int> PurgeAlbumPlaceholdersAsync(DateTimeOffset? now = null) => WriteAsync(connection =>
    {
        var cutoff = UnixMilliseconds.From((now ?? DateTimeOffset.UtcNow) - AlbumPlaceholderLifetime);
        return Sql.Execute(connection, "DELETE FROM album_item WHERE asset_id IN (SELECT id FROM asset WHERE state = 1 AND missing_since < @cutoff)",
            ("@cutoff", cutoff));
    });

    private static List<long> AlbumOrder(SqliteConnection connection, long albumId) =>
        Sql.Rows(connection, "SELECT asset_id FROM album_item WHERE album_id = @album ORDER BY position, asset_id", r => r.GetInt64(0), ("@album", albumId));

    private static void Renumber(SqliteConnection connection, long albumId, IReadOnlyList<long> order)
    {
        var current = AlbumOrder(connection, albumId);
        var known = current.ToHashSet();
        var full = order.Where(known.Contains).Distinct().Concat(current.Where(id => !order.Contains(id))).ToList();
        for (var position = 0; position < full.Count; position++)
        {
            Sql.Execute(connection, "UPDATE album_item SET position = @position WHERE album_id = @album AND asset_id = @asset",
                ("@position", position), ("@album", albumId), ("@asset", full[position]));
        }
    }

    private static IReadOnlyList<Album> FetchAlbums(SqliteConnection connection) => Sql.Rows(connection, """
        SELECT album.id, album.name, album.created_at,
          COALESCE(
            (SELECT a.id FROM asset a WHERE a.id = album.cover_asset_id AND a.state = 0),
            (SELECT i.asset_id FROM album_item i JOIN asset a ON a.id = i.asset_id
             WHERE i.album_id = album.id AND a.state = 0 ORDER BY i.position LIMIT 1)) AS cover,
          (SELECT COUNT(*) FROM album_item i JOIN asset a ON a.id = i.asset_id WHERE i.album_id = album.id AND a.state = 0)
        FROM album ORDER BY album.sort_order, album.id
        """, r => new Album(r.GetInt64(0), r.GetString(1), UnixMilliseconds.ToDate(r.GetInt64(2)),
            r.IsDBNull(3) ? null : r.GetInt64(3), r.GetInt32(4)));
}
