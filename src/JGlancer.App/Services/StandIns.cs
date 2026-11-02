using JGlancer.Core.Model;
using JGlancer.Core.Scanning;

namespace JGlancer.App.Services;

/// <summary>
/// Rows for files opened from outside the library (macOS D-63, D-65): a negative id per path, made from the file
/// itself, kept in memory only (nothing is written to the library). Thumbnails of negative ids skip the disk cache.
/// UI thread only.
/// </summary>
internal sealed class StandIns
{
    private readonly Dictionary<long, Asset> _byId = [];
    private readonly Dictionary<string, long> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private long _next = -1;

    public static bool IsStandIn(long id) => id < 0;

    /// <summary>The stand-in id of <paramref name="path"/> (made once per path); null when it is not a media file.</summary>
    public long? Add(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_byPath.TryGetValue(path, out var known))
        {
            return known;
        }
        if (MediaClassifier.Classify(path) is not { } type)
        {
            return null;
        }
        var info = new FileInfo(path);
        var id = _next--;
        _byId[id] = new Asset(id, 0, info.Name, info.FullName, type.Kind, type.TypeIdentifier,
            info.Exists ? info.Length : 0,
            info.Exists ? new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero) : DateTimeOffset.UnixEpoch,
            info.Exists ? new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero) : null,
            null, null, null, null, null, null, null, AssetState.Present, MetadataState.Pending, AssetFlag.None);
        _byPath[path] = id;
        return id;
    }

    public Asset? Get(long id) => _byId.GetValueOrDefault(id);
}

/// <summary>Rows for library ids and stand-ins alike.</summary>
internal static class AssetRows
{
    /// <summary>The rows of <paramref name="ids"/>: the library's from the store, stand-ins from memory.</summary>
    public static async Task<IReadOnlyList<Asset>> AssetsAsync(this AppServices services, IReadOnlyList<long> ids)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(ids);
        var library = ids.Where(id => id > 0).ToList();
        var rows = library.Count == 0 ? [] : await services.Store.AssetsAsync(library);
        if (library.Count == ids.Count)
        {
            return rows;
        }
        return [.. rows, .. ids.Where(StandIns.IsStandIn).Select(services.StandIns.Get).OfType<Asset>()];
    }
}
