using JGlancer.Core.Model;
using JGlancer.Core.Store;

namespace JGlancer.Core.Tests.Store;

/// <summary>A file system made of explicit facts, for identity-matching tests.</summary>
internal sealed class FakeProbe : IFileProbe
{
    public HashSet<string> ExistingPaths { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, byte[]> Hashes { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FileExists(string path) => ExistingPaths.Contains(path);

    public byte[]? QuickHash(string path) => Hashes.GetValueOrDefault(path);
}

internal static class StoreTestSupport
{
    public static readonly DateTimeOffset Base = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    public static ScannedFile File(string name, long size = 100, double modified = 0, ulong? fileId = null, MediaKind kind = MediaKind.Photo) =>
        new(name, kind, null, size, Base.AddSeconds(modified), null, fileId);

    /// <summary>Applies a listing of <paramref name="relativePath"/> in root <paramref name="rootId"/>.</summary>
    public static Task<DirectoryScanResult> ScanAsync(
        this LibraryStore store, long rootId, string relativePath, IReadOnlyList<ScannedFile> files,
        IReadOnlyList<string>? subdirectories = null, FakeProbe? probe = null) =>
        store.ApplyDirectoryScanAsync(new DirectoryScan(rootId, relativePath, files, subdirectories ?? [], Base), probe ?? new FakeProbe());

    public static Task<DirectoryScanResult> ScanAsync(
        this LibraryStore store, long rootId, IReadOnlyList<ScannedFile> files, IReadOnlyList<string>? subdirectories = null, FakeProbe? probe = null) =>
        store.ScanAsync(rootId, "", files, subdirectories, probe);

    /// <summary>Present asset ids by name across the library.</summary>
    public static async Task<Dictionary<string, long>> IdsByNameAsync(this LibraryStore store)
    {
        var refs = await store.AssetRefsAsync(new AssetQuery());
        var assets = await store.AssetsAsync(refs.Select(r => r.Id).ToList());
        return assets.ToDictionary(a => a.Name, a => a.Id);
    }

    public static async Task<List<string>> NamesAsync(this LibraryStore store, AssetQuery query)
    {
        var refs = await store.AssetRefsAsync(query);
        return (await store.AssetsAsync(refs.Select(r => r.Id).ToList())).Select(a => a.Name).ToList();
    }
}
