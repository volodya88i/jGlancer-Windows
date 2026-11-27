using JGlancer.Core.Model;
using Microsoft.Data.Sqlite;

namespace JGlancer.Core.Store;

public sealed partial class LibraryStore
{
    /// <summary>The icons of the library folders that have one, by root id (macOS D-129).</summary>
    public Task<IReadOnlyDictionary<long, FolderIcon>> RootIconsAsync() => ReadAsync(FetchRootIcons);

    /// <summary>The icons now and after every change.</summary>
    public IAsyncEnumerable<IReadOnlyDictionary<long, FolderIcon>> ObserveRootIcons(CancellationToken cancellation = default) =>
        Observe(FetchRootIcons, SameIcons, cancellation);

    /// <summary>Gives a library folder its own icon, or the default one back (<c>null</c>).</summary>
    public Task SetRootIconAsync(long rootId, FolderIcon? icon) => WriteAsync(connection =>
    {
        if (icon is null)
        {
            Sql.Execute(connection, "DELETE FROM root_icon WHERE root_id = @root", ("@root", rootId));
            return;
        }
        var (kind, value, picture) = icon switch
        {
            FolderIcon.Symbol symbol => ("symbol", symbol.Name, (byte[]?)null),
            FolderIcon.Emoji emoji => ("emoji", emoji.Text, null),
            FolderIcon.Picture image => ("picture", "", image.Jpeg),
            _ => throw new ArgumentOutOfRangeException(nameof(icon)),
        };
        Sql.Execute(connection, "INSERT OR REPLACE INTO root_icon (root_id, kind, value, picture) VALUES (@root, @kind, @value, @picture)",
            ("@root", rootId), ("@kind", kind), ("@value", value), ("@picture", picture));
    });

    private static IReadOnlyDictionary<long, FolderIcon> FetchRootIcons(SqliteConnection connection)
    {
        var icons = new Dictionary<long, FolderIcon>();
        foreach (var (id, icon) in Sql.Rows(connection, "SELECT root_id, kind, value, picture FROM root_icon", r =>
        {
            var value = r.GetString(2);
            FolderIcon? icon = r.GetString(1) switch
            {
                "symbol" => new FolderIcon.Symbol(value),
                "emoji" => new FolderIcon.Emoji(value),
                "picture" when !r.IsDBNull(3) => new FolderIcon.Picture((byte[])r.GetValue(3)),
                _ => null,
            };
            return (r.GetInt64(0), icon);
        }))
        {
            if (icon is not null)
            {
                icons[id] = icon;
            }
        }
        return icons;
    }

    private static bool SameIcons(IReadOnlyDictionary<long, FolderIcon> a, IReadOnlyDictionary<long, FolderIcon> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var other) && Same(pair.Value, other));

    private static bool Same(FolderIcon a, FolderIcon b) =>
        a is FolderIcon.Picture pa && b is FolderIcon.Picture pb ? pa.Jpeg.AsSpan().SequenceEqual(pb.Jpeg) : a == b;
}
