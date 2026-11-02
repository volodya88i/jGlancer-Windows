using System.Globalization;
using JGlancer.Core.Model;
using Microsoft.Data.Sqlite;

namespace JGlancer.Core.Store;

/// <summary>Counts describing the library, for status bars and benchmarks.</summary>
public sealed record LibraryStatistics(IReadOnlyDictionary<MediaKind, int> AssetsByKind, int MissingAssets, int PendingMetadata, int Folders)
{
    /// <summary>Present assets of every kind.</summary>
    public int PresentAssets => AssetsByKind.Values.Sum();
}

public sealed partial class LibraryStore
{
    /// <summary>The lean, sorted index of present assets matching <paramref name="query"/>.</summary>
    public Task<IReadOnlyList<AssetRef>> AssetRefsAsync(AssetQuery query) => ReadAsync(connection => FetchAssetRefs(connection, query));

    /// <summary>Full rows for <paramref name="ids"/>, in the order of <paramref name="ids"/>; unknown ids are skipped.</summary>
    public Task<IReadOnlyList<Asset>> AssetsAsync(IReadOnlyList<long> ids) => ReadAsync<IReadOnlyList<Asset>>(connection =>
    {
        var rows = new Dictionary<long, AssetRow>();
        foreach (var chunk in Sql.Chunks(ids))
        {
            var (list, parameters) = Sql.InList(chunk);
            foreach (var row in Sql.Rows(connection, $"SELECT {AssetRow.Columns} FROM asset WHERE id IN ({list})", AssetRow.Read, parameters))
            {
                rows[row.Id] = row;
            }
        }
        var directories = FolderPaths(connection, rows.Values.Select(r => r.FolderId).Distinct().ToList());
        return ids.Where(rows.ContainsKey)
            .Select(id => rows[id])
            .Where(row => directories.ContainsKey(row.FolderId))
            .Select(row => row.ToModel(directories[row.FolderId]))
            .ToList();
    });

    /// <summary>Number and total size in bytes of the assets matching <paramref name="query"/>.</summary>
    public Task<(int Assets, long Bytes)> TotalsAsync(AssetQuery query) => ReadAsync(connection =>
    {
        if (Filter(query) is not { } filter)
        {
            return (0, 0L);
        }
        using var command = Sql.Command(connection, $"SELECT COUNT(*), TOTAL(size) FROM asset WHERE {filter.Sql}", filter.Parameters);
        using var reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetInt32(0), (long)reader.GetDouble(1));
    });

    /// <summary>Total size in bytes of the given assets.</summary>
    public Task<long> TotalSizeAsync(IReadOnlyList<long> ids) => ReadAsync(connection =>
    {
        double total = 0;
        foreach (var chunk in Sql.Chunks(ids))
        {
            var (list, parameters) = Sql.InList(chunk);
            total += Convert.ToDouble(Sql.Scalar(connection, $"SELECT TOTAL(size) FROM asset WHERE id IN ({list})", parameters) ?? 0d, CultureInfo.InvariantCulture);
        }
        return (long)total;
    });

    /// <summary>Absolute path of an asset's file, or null if the id is unknown.</summary>
    public Task<string?> FilePathAsync(long assetId) => ReadAsync(connection => Sql.Rows(connection, """
        SELECT root_folder.path, folder.rel_path, asset.name
        FROM asset JOIN folder ON folder.id = asset.folder_id JOIN root_folder ON root_folder.id = folder.root_id
        WHERE asset.id = @id
        """, r => LibraryPaths.Combine(LibraryPaths.Absolute(r.GetString(0), r.GetString(1)), r.GetString(2)), ("@id", assetId)).FirstOrDefault());

    /// <summary>Folders of a root (present ones unless <paramref name="includeMissing"/>), sorted by relative path.</summary>
    public Task<IReadOnlyList<Folder>> FoldersAsync(long rootId, bool includeMissing = false) =>
        ReadAsync(connection => FetchFolders(connection, rootId, includeMissing));

    /// <summary>Counts by kind and state.</summary>
    public Task<LibraryStatistics> StatisticsAsync() => ReadAsync(connection =>
    {
        var byKind = new Dictionary<MediaKind, int>();
        foreach (var (kind, count) in Sql.Rows(connection, "SELECT kind, COUNT(*) FROM asset WHERE state = 0 GROUP BY kind", r => (r.GetInt32(0), r.GetInt32(1))))
        {
            byKind[(MediaKind)kind] = count;
        }
        return new LibraryStatistics(
            byKind,
            (int)(Sql.Long(connection, "SELECT COUNT(*) FROM asset WHERE state = 1") ?? 0),
            (int)(Sql.Long(connection, "SELECT COUNT(*) FROM asset WHERE meta_state = 0 AND state = 0") ?? 0),
            (int)(Sql.Long(connection, "SELECT COUNT(*) FROM folder WHERE state = 0") ?? 0));
    });

    internal static IReadOnlyList<Folder> FetchFolders(SqliteConnection connection, long rootId, bool includeMissing) =>
        Sql.Rows(connection,
            $"SELECT {FolderRow.Columns} FROM folder WHERE root_id = @root {(includeMissing ? "" : "AND state = 0")} ORDER BY rel_path",
            r => FolderRow.Read(r).ToModel(), ("@root", rootId));

    /// <summary>Absolute paths of folders by id.</summary>
    internal static Dictionary<long, string> FolderPaths(SqliteConnection connection, IReadOnlyList<long> folderIds)
    {
        var paths = new Dictionary<long, string>();
        foreach (var chunk in Sql.Chunks(folderIds))
        {
            var (list, parameters) = Sql.InList(chunk);
            foreach (var (id, path) in Sql.Rows(connection, $"""
                SELECT folder.id, folder.rel_path, root_folder.path FROM folder
                JOIN root_folder ON root_folder.id = folder.root_id WHERE folder.id IN ({list})
                """, r => (r.GetInt64(0), LibraryPaths.Absolute(r.GetString(2), r.GetString(1))), parameters))
            {
                paths[id] = path;
            }
        }
        return paths;
    }

    /// <summary>WHERE clause selecting the present assets of <paramref name="query"/>; null when it matches nothing.</summary>
    internal static (string Sql, (string, object?)[] Parameters)? Filter(AssetQuery query)
    {
        var kinds = query.Kinds.Select(k => (int)k).Order().ToList();
        if (kinds.Count == 0)
        {
            return null;
        }
        var conditions = new List<string> { "asset.state = 0", $"asset.kind IN ({string.Join(",", kinds)})" };
        var parameters = new List<(string, object?)>();
        if (ScopeCondition(query.Scope) is { } scope)
        {
            conditions.Add(scope);
            parameters.Add(("@scope", ScopeId(query.Scope)));
        }
        if (query.Flags is { } flags)
        {
            conditions.Add(flags.Only is { } flag ? $"asset.flag = {(int)flag}" : "asset.flag != 0");
        }
        if (SearchCondition(query.SearchText) is { } search)
        {
            conditions.Add(search.Sql);
            parameters.Add(("@search", search.Argument));
        }
        return (string.Join(" AND ", conditions), parameters.ToArray());
    }

    /// <summary>Three characters or more use the trigram index (a quoted phrase); shorter text a plain LIKE.</summary>
    internal static (string Sql, string Argument)? SearchCondition(string? text)
    {
        if (text is null)
        {
            return null;
        }
        if (text.Length >= 3)
        {
            return ("asset.id IN (SELECT rowid FROM asset_search WHERE asset_search MATCH @search)",
                "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"");
        }
        var escaped = text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return ("asset.name LIKE @search ESCAPE '\\'", "%" + escaped + "%");
    }

    internal static IReadOnlyList<AssetRef> FetchAssetRefs(SqliteConnection connection, AssetQuery query)
    {
        if (Filter(query) is not { } filter)
        {
            return [];
        }
        var direction = query.Ascending ? "ASC" : "DESC";
        var order = query.Scope is QueryScope.AlbumScope
            ? "(SELECT position FROM album_item WHERE album_id = @scope AND asset_id = asset.id), asset.id"
            : string.Join(", ", OrderColumns(query.Sort).Select(c => $"{c} {direction}"));
        return Sql.Rows(connection, $"SELECT asset.id, asset.kind FROM asset WHERE {filter.Sql} ORDER BY {order}",
            r => new AssetRef(r.GetInt64(0), (MediaKind)r.GetInt32(1)), filter.Parameters);
    }

    private static long ScopeId(QueryScope scope) => scope switch
    {
        QueryScope.RootScope root => root.Id,
        QueryScope.FolderScope folder => folder.Id,
        QueryScope.AlbumScope album => album.Id,
        _ => 0,
    };

    /// <summary>SQL restricting assets to a scope (single <c>@scope</c> argument); null for the whole library.</summary>
    private static string? ScopeCondition(QueryScope scope) => scope switch
    {
        QueryScope.RootScope => "asset.folder_id IN (SELECT id FROM folder WHERE root_id = @scope)",
        QueryScope.AlbumScope => "asset.id IN (SELECT asset_id FROM album_item WHERE album_id = @scope)",
        QueryScope.FolderScope { IncludeSubfolders: false } => "asset.folder_id = @scope",
        // Subtree via a range on rel_path ("a/b/" ..< "a/b0"), which uses the (root_id, rel_path) index.
        QueryScope.FolderScope => """
            asset.folder_id IN (
              SELECT f.id FROM folder f JOIN folder p ON p.id = @scope AND f.root_id = p.root_id
              WHERE f.id = p.id OR p.rel_path = '' OR (f.rel_path >= p.rel_path || '/' AND f.rel_path < p.rel_path || '0'))
            """,
        _ => null,
    };

    /// <summary>Sort columns; ties fall back to natural name order, then id.</summary>
    private static string[] OrderColumns(AssetSort sort) => sort switch
    {
        AssetSort.DateModified => ["asset.mtime", "asset.name_key", "asset.id"],
        AssetSort.DateCaptured => ["COALESCE(asset.captured_at, asset.mtime)", "asset.name_key", "asset.id"],
        AssetSort.Size => ["asset.size", "asset.name_key", "asset.id"],
        AssetSort.Kind => ["asset.kind", "asset.name_key", "asset.id"],
        _ => ["asset.name_key", "asset.name", "asset.id"],
    };
}
