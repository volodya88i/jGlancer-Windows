using JGlancer.Core.Model;
using Microsoft.Data.Sqlite;

namespace JGlancer.Core.Store;

/// <summary>Flags (macOS LibraryStore+Flags, D-57), kept in the library only: nothing is written to the files (D-15).</summary>
public sealed partial class LibraryStore
{
    /// <summary>Sets <paramref name="flag"/> on the assets; returns each one's previous flag (for undo).</summary>
    public Task<IReadOnlyDictionary<long, AssetFlag>> SetFlagAsync(AssetFlag flag, IReadOnlyList<long> assetIds)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        return WriteAsync<IReadOnlyDictionary<long, AssetFlag>>(connection =>
        {
            var previous = FetchFlags(connection, assetIds);
            foreach (var chunk in Sql.Chunks(assetIds))
            {
                var (list, parameters) = Sql.InList(chunk);
                Sql.Execute(connection, $"UPDATE asset SET flag = @flag WHERE id IN ({list})", [.. parameters, ("@flag", (int)flag)]);
            }
            return previous;
        });
    }

    /// <summary>Puts back flags returned by <see cref="SetFlagAsync"/>.</summary>
    public Task RestoreFlagsAsync(IReadOnlyDictionary<long, AssetFlag> flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        return WriteAsync(connection =>
        {
            foreach (var (id, flag) in flags)
            {
                Sql.Execute(connection, "UPDATE asset SET flag = @flag WHERE id = @id", ("@flag", (int)flag), ("@id", id));
            }
        });
    }

    /// <summary>The flags of the assets that exist.</summary>
    public Task<IReadOnlyDictionary<long, AssetFlag>> FlagsAsync(IReadOnlyList<long> assetIds)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        return ReadAsync<IReadOnlyDictionary<long, AssetFlag>>(connection => FetchFlags(connection, assetIds));
    }

    private static Dictionary<long, AssetFlag> FetchFlags(SqliteConnection connection, IReadOnlyList<long> assetIds)
    {
        var flags = new Dictionary<long, AssetFlag>();
        foreach (var chunk in Sql.Chunks(assetIds))
        {
            var (list, parameters) = Sql.InList(chunk);
            foreach (var (id, flag) in Sql.Rows(connection, $"SELECT id, flag FROM asset WHERE id IN ({list})",
                r => (r.GetInt64(0), r.GetInt32(1)), parameters))
            {
                flags[id] = Enum.IsDefined((AssetFlag)flag) ? (AssetFlag)flag : AssetFlag.None;
            }
        }
        return flags;
    }
}
