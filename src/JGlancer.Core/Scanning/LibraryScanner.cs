using System.Diagnostics;
using JGlancer.Core.Model;
using JGlancer.Core.Store;

namespace JGlancer.Core.Scanning;

/// <summary>
/// Indexes library roots: stage 1 (directory listing), stage 2 (metadata) and live watching (macOS ARCHITECTURE §5).
/// Only reads files; never modifies them.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The pause gate never creates a kernel handle (only Wait, Set and Reset) and lives as long as the app.")]
public sealed class LibraryScanner(LibraryStore store, DirectoryLister lister, IMetadataReader metadata, FolderWatcher? watcher = null)
{
    private readonly DiskFileProbe _probe = new();
    private readonly ManualResetEventSlim _metadataRuns = new(initialState: true);
    private int _metadataPauses;

    public LibraryStore Store { get; } = store;

    public DirectoryLister Lister { get; } = lister;

    public FolderWatcher Watcher { get; } = watcher ?? new FolderWatcher();

    /// <summary>
    /// Holds stage 2 back until the returned object is disposed (the viewer decoding a photo, W12): its file reads
    /// would compete for the disk. Pauses nest; files being read finish first.
    /// </summary>
    public IDisposable PauseMetadata()
    {
        if (Interlocked.Increment(ref _metadataPauses) == 1)
        {
            _metadataRuns.Reset();
        }
        return new MetadataPause(this);
    }

    private void ResumeMetadata()
    {
        if (Interlocked.Decrement(ref _metadataPauses) == 0)
        {
            _metadataRuns.Set();
        }
    }

    private sealed class MetadataPause(LibraryScanner scanner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                scanner.ResumeMetadata();
            }
        }
    }

    // MARK: Roots

    /// <summary>Adds <paramref name="path"/> as a library root and runs stage 1 on it.</summary>
    public async Task<(RootFolder Root, ScanReport Report)> AddRootAsync(string path, bool isRecursive = true, CancellationToken cancellation = default)
    {
        var root = await RegisterRootAsync(path, isRecursive).ConfigureAwait(false);
        return (root, await ScanAsync(root.Id, cancellation).ConfigureAwait(false));
    }

    /// <summary>Adds <paramref name="path"/> as a library root without scanning it (<see cref="WatchAsync"/> scans it first).</summary>
    public Task<RootFolder> RegisterRootAsync(string path, bool isRecursive = true)
    {
        if (!Directory.Exists(path))
        {
            throw new RootUnavailableException(path);
        }
        var canonical = Lister.Reader.Canonical(path);
        return Store.AddRootAsync(canonical, Lister.Reader.VolumeId(canonical), isRecursive);
    }

    // MARK: Stage 1

    /// <summary>Diff-scans a whole root against the database.</summary>
    public async Task<ScanReport> ScanAsync(long rootId, CancellationToken cancellation = default)
    {
        var root = await Store.RootAsync(rootId).ConfigureAwait(false);
        if (!Directory.Exists(root.Path))
        {
            throw new RootUnavailableException(root.Path);
        }
        return await ScanAsync(root, "", root.IsRecursive, cancellation).ConfigureAwait(false);
    }

    /// <param name="progress">Called after each directory (on the scanning thread).</param>
    private async Task<ScanReport> ScanAsync(
        RootFolder root, string relativePath, bool recursive, CancellationToken cancellation, Action<ScanProgress>? progress = null)
    {
        var clock = Stopwatch.StartNew();
        var report = new ScanReport();
        var queue = new Stack<string>();
        queue.Push(relativePath);
        while (queue.TryPop(out var current))
        {
            cancellation.ThrowIfCancellationRequested();
            DirectoryScan listing;
            try
            {
                listing = await Task.Run(() => Lister.List(LibraryPaths.Absolute(root.Path, current), root.Id, current), cancellation).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                report.UnreadableDirectories++;
                continue;
            }
            report.Add(await Store.ApplyDirectoryScanAsync(listing, _probe).ConfigureAwait(false));
            progress?.Invoke(new ScanProgress(report.Directories, report.Added + report.Updated + report.Moved + report.Revived + report.Unchanged));
            if (recursive)
            {
                foreach (var name in listing.Subdirectories)
                {
                    queue.Push(current.Length == 0 ? name : current + "/" + name);
                }
            }
        }
        report.Duration = clock.Elapsed;
        return report;
    }

    // MARK: Stage 2

    /// <summary>
    /// Extracts metadata for every pending asset, <paramref name="concurrency"/> files at a time on the thread pool.
    /// Returns the number of assets processed. Cancellable between files. By default 2–3 files at a time on threads of
    /// below-normal priority: it is background work and must leave the viewer, the grid and thumbnails room (W-37).
    /// </summary>
    public async Task<int> ExtractMetadataAsync(int batchSize = 128, int concurrency = 0, CancellationToken cancellation = default)
    {
        var parallelism = concurrency > 0 ? concurrency : Math.Clamp(Environment.ProcessorCount / 8, 2, 3);
        long afterId = 0;
        var processed = 0;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var batch = await Store.PendingMetadataAsync(afterId, batchSize).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                return processed;
            }
            afterId = batch[^1].Id;
            var updates = new MetadataUpdate[batch.Count];
            await Parallel.ForEachAsync(Enumerable.Range(0, batch.Count),
                new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = cancellation },
                (index, token) =>
                {
                    // Paused while the viewer decodes (PauseMetadata).
                    _metadataRuns.Wait(token);
                    var item = batch[index];
                    var thread = Thread.CurrentThread;
                    var previous = thread.Priority;
                    thread.Priority = ThreadPriority.BelowNormal;
                    try
                    {
                        updates[index] = metadata.Read(item) with { QuickHash = QuickHash.Compute(item.Path) };
                    }
                    finally
                    {
                        thread.Priority = previous;
                    }
                    return ValueTask.CompletedTask;
                }).ConfigureAwait(false);
            await Store.SaveMetadataAsync(updates).ConfigureAwait(false);
            processed += updates.Length;
        }
    }

    // MARK: Watching

    /// <summary>
    /// Diff-scans a root, then watches it until <paramref name="cancellation"/> fires, rescanning changed directories.
    /// There is no event history on Windows, so the first scan is what catches changes made while the app was closed.
    /// </summary>
    public Task WatchAsync(long rootId, Action<WatchUpdate>? onUpdate = null, CancellationToken cancellation = default) =>
        WatchAsync(rootId, onUpdate, progress: null, cancellation);

    /// <param name="progress">The first scan's progress, after each directory (on the scanning thread).</param>
    public async Task WatchAsync(long rootId, Action<WatchUpdate>? onUpdate, Action<ScanProgress>? progress, CancellationToken cancellation)
    {
        var root = await Store.RootAsync(rootId).ConfigureAwait(false);
        // Start watching before the scan so nothing that changes during it is lost.
        await using var events = Watcher.Events(root.Path, cancellation).GetAsyncEnumerator(cancellation);
        if (!Directory.Exists(root.Path))
        {
            throw new RootUnavailableException(root.Path);
        }
        var initial = await ScanAsync(root, "", root.IsRecursive, cancellation, progress).ConfigureAwait(false);
        onUpdate?.Invoke(new WatchUpdate([], [new RescanTarget("", true)], initial));
        while (await events.MoveNextAsync().ConfigureAwait(false))
        {
            var targets = ChangePlanner.Plan(events.Current, root.Path);
            var report = new ScanReport();
            foreach (var target in targets)
            {
                report.Add(await RescanAsync(root, target, cancellation).ConfigureAwait(false));
            }
            onUpdate?.Invoke(new WatchUpdate(events.Current, targets, report));
        }
    }

    private async Task<ScanReport> RescanAsync(RootFolder root, RescanTarget target, CancellationToken cancellation)
    {
        if (!root.IsRecursive && target.RelativePath.Length > 0)
        {
            return new ScanReport();
        }
        // A vanished directory is handled by rescanning its nearest existing ancestor, which tombstones it.
        var relativePath = target.RelativePath;
        var recursive = target.Recursive && root.IsRecursive;
        while (relativePath.Length > 0 && !Directory.Exists(LibraryPaths.Absolute(root.Path, relativePath)))
        {
            relativePath = LibraryPaths.ParentOf(relativePath);
            recursive = false;
        }
        if (!Directory.Exists(root.Path))
        {
            throw new RootUnavailableException(root.Path);
        }
        return await ScanAsync(root, relativePath, recursive, cancellation).ConfigureAwait(false);
    }
}
