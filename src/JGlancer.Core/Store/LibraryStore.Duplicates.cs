namespace JGlancer.Core.Store;

public sealed partial class LibraryStore
{
    /// <summary>
    /// Files that are most likely copies of each other (W-78): present files with the same size and the same quick hash
    /// (stage 2 computes it; files still waiting for it are not counted yet). Groups of ids, the biggest files first,
    /// each group in path order. Not proof: <see cref="Scanning.FileContent.SameAsync"/> compares two files fully.
    /// </summary>
    public Task<IReadOnlyList<IReadOnlyList<long>>> DuplicateGroupsAsync() => ReadAsync<IReadOnlyList<IReadOnlyList<long>>>(connection =>
    {
        var rows = Sql.Rows(connection, """
            SELECT asset.id, asset.size, hex(asset.quick_hash)
            FROM asset JOIN folder ON folder.id = asset.folder_id
            WHERE asset.state = 0 AND asset.quick_hash IS NOT NULL AND asset.size > 0
              AND (asset.size, asset.quick_hash) IN (
                SELECT size, quick_hash FROM asset
                WHERE state = 0 AND quick_hash IS NOT NULL AND size > 0
                GROUP BY size, quick_hash HAVING COUNT(*) > 1)
            ORDER BY asset.size DESC, asset.quick_hash, folder.root_id, folder.rel_path, asset.name_key
            """, r => (Id: r.GetInt64(0), Size: r.GetInt64(1), Hash: r.GetString(2)));
        var groups = new List<IReadOnlyList<long>>();
        foreach (var group in rows.GroupBy(r => (r.Size, r.Hash)))
        {
            groups.Add([.. group.Select(r => r.Id)]);
        }
        return groups;
    });
}
