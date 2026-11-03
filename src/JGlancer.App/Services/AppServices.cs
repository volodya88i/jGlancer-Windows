using JGlancer.App.Developer;
using JGlancer.Core;
using JGlancer.Core.Scanning;
using JGlancer.Core.Store;
using JGlancer.App.Library;
using JGlancer.App.Support;
using JGlancer.Media.Photos;
using JGlancer.Media.Thumbnails;
using JGlancer.Platform.Windows;

namespace JGlancer.App.Services;

/// <summary>Where the app keeps its files: user data in %APPDATA%\jGlancer, rebuildable caches in %LOCALAPPDATA%.</summary>
internal sealed record AppPaths(string DataDirectory, string CacheDirectory)
{
    public string Library => Path.Combine(DataDirectory, "library.sqlite");
    public string LibraryBackup => Path.Combine(DataDirectory, "library-backup.sqlite");
    public string ViewState => Path.Combine(DataDirectory, "view-state.json");

    /// <summary>The split view's second pane: its folder, filters, sort, size and selection.</summary>
    public string SplitViewState => Path.Combine(DataDirectory, "view-state-split.json");
    public string PlaybackPreferences => Path.Combine(DataDirectory, "playback.json");
    public string OpenWith => Path.Combine(DataDirectory, "open-with.json");
    public string RecentFolders => Path.Combine(DataDirectory, "recent-folders.json");
    public string Settings => Path.Combine(DataDirectory, "settings.json");
    public string Shortcuts => Path.Combine(DataDirectory, "shortcuts.json");
    public string ThumbnailCache => Path.Combine(CacheDirectory, "cache.sqlite");

    /// <summary>The stem splitter's downloaded models (W-85): %LOCALAPPDATA%\jGlancer\Models, beside the cache.</summary>
    public string Models => Path.Combine(Path.GetDirectoryName(CacheDirectory) ?? CacheDirectory, "Models");

    public static AppPaths For(DeveloperOptions options) => options.DataDirectory is { } folder
        ? new AppPaths(Path.GetFullPath(folder), Path.Combine(Path.GetFullPath(folder), "Cache"))
        : new AppPaths(AppIdentity.DataDirectory, AppIdentity.CacheDirectory);
}

/// <summary>The long-lived services of one app run, created once at start.</summary>
internal sealed class AppServices : IDisposable
{
    private AppServices(AppPaths paths, LibraryStore store, LibraryRecovery? recovery, ThumbnailDiskCache cache)
    {
        Paths = paths;
        Store = store;
        Viewer.Media.AudioAnalyzer.Store = store;
        Recovery = recovery;
        Cache = cache;
        Thumbnails = new ThumbnailService(cache, new WindowsThumbnailGenerator());
        Scanner = new LibraryScanner(store, new DirectoryLister(new WindowsDirectoryReader()), new WindowsMetadataReader());
    }

    public AppPaths Paths { get; }
    public LibraryStore Store { get; }

    /// <summary>Rows of files opened from outside the library (negative ids).</summary>
    public StandIns StandIns { get; } = new();

    /// <summary>What happened if the library database had to be restored or replaced at start.</summary>
    public LibraryRecovery? Recovery { get; }

    public ThumbnailDiskCache Cache { get; }
    public ThumbnailService Thumbnails { get; }

    /// <summary>Display-ready thumbnails in memory (grid, filmstrip, viewer placeholders); UI thread only.</summary>
    public ThumbnailCache Bitmaps { get; } = new(96L * 1024 * 1024);

    /// <summary>Remembered speed per kind, volume, Keep Pitch and "play next" (playback.json).</summary>
    public JsonFile PlaybackPreferences { get; private init; } = null!;

    /// <summary>Programs chosen for Open With per kind and their primaries (open-with.json).</summary>
    public JsonFile OpenWith { get; private init; } = null!;

    /// <summary>Recent destinations of Move to… and Copy to… (recent-folders.json).</summary>
    public JsonFile RecentFolders { get; private init; } = null!;

    /// <summary>Screen-size and full-resolution photo decodes for the viewer.</summary>
    public IPhotoDecoder Photos { get; } = new WicPhotoDecoder();
    public LibraryScanner Scanner { get; }

    /// <summary>Opens the library (with recovery, macOS D-66) and the thumbnail cache.</summary>
    /// <param name="isPersistent">False for benchmark and snapshot runs: settings files are never written.</param>
    public static AppServices Start(AppPaths paths, bool isPersistent = true)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Directory.CreateDirectory(paths.DataDirectory);
        var (store, recovery) = LibraryStore.OpenWithRecovery(paths.Library, paths.LibraryBackup);
        return new AppServices(paths, store, recovery, ThumbnailDiskCache.Open(paths.ThumbnailCache))
        {
            PlaybackPreferences = new JsonFile(paths.PlaybackPreferences, isPersistent),
            OpenWith = new JsonFile(paths.OpenWith, isPersistent),
            RecentFolders = new JsonFile(paths.RecentFolders, isPersistent),
        };
    }

    /// <summary>The daily backup of the library, in the background.</summary>
    public Task BackupAsync() => Store.BackupAsync(Paths.LibraryBackup, TimeSpan.FromDays(1));

    public void Dispose()
    {
        Store.Dispose();
        Cache.Dispose();
    }
}
