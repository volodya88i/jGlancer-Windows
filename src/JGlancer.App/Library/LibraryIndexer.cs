using JGlancer.Core.Model;
using JGlancer.Core.Scanning;

namespace JGlancer.App.Library;

/// <summary>Background work shown in the status bar.</summary>
internal enum LibraryActivity
{
    None,
    Indexing,
    ReadingDetails,
}

/// <summary>
/// Keeps the library in sync in the background (macOS LibraryIndexer): one watch per root, which starts with a
/// diff-scan, and stage-2 metadata after every change. Lives on the UI thread; the work itself runs on the pool.
/// A folder just added is in its first scan until it ends: <see cref="FirstScans"/> with its progress (W-70).
/// </summary>
internal sealed class LibraryIndexer(LibraryScanner scanner)
{
    private readonly Dictionary<long, CancellationTokenSource> _watches = [];
    private readonly HashSet<long> _indexing = [];
    private readonly Dictionary<long, FirstScan> _firstScans = [];

    /// <summary>How often a first scan's progress reaches the screen.</summary>
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>A folder being added: where it is and how far its first scan got.</summary>
    public sealed record FirstScan(string Path, ScanProgress Progress);

    /// <summary>Folders just added whose first scan runs (by root id).</summary>
    public IReadOnlyDictionary<long, FirstScan> FirstScans => _firstScans;

    /// <summary>A first scan ended (or failed, or was stopped): the folder may show (UI thread).</summary>
    public event EventHandler<long>? FirstScanEnded;
    private Task? _metadata;
    private bool _metadataRequested;

    /// <summary>Raised on the UI thread when <see cref="Activity"/> or <see cref="OfflineRoots"/> change.</summary>
    public event EventHandler? Changed;

    public LibraryActivity Activity { get; private set; }

    /// <summary>Roots whose folder is unavailable (unplugged drive, moved or deleted).</summary>
    public HashSet<long> OfflineRoots { get; } = [];

    /// <param name="isNew">Just added: its first scan reports progress (<see cref="FirstScans"/>).</param>
    public void StartWatching(RootFolder root, bool isNew = false)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (_watches.ContainsKey(root.Id))
        {
            return;
        }
        var stop = new CancellationTokenSource();
        _watches[root.Id] = stop;
        _indexing.Add(root.Id);
        Action<ScanProgress>? progress = null;
        var context = SynchronizationContext.Current;
        if (isNew)
        {
            _firstScans[root.Id] = new FirstScan(root.Path, default);
            var last = 0L;
            progress = scanned =>
            {
                // Throttled: a drive lists thousands of folders a second.
                var now = Environment.TickCount64;
                if (now - Interlocked.Read(ref last) >= ProgressInterval.TotalMilliseconds)
                {
                    Interlocked.Exchange(ref last, now);
                    context?.Post(_ => Progressed(root.Id, scanned), null);
                }
            };
        }
        Update();
        void OnUpdate(WatchUpdate update) => context?.Post(_ => Indexed(root.Id, isOnline: true), null);
        _ = WatchAsync(root.Id, OnUpdate, progress, stop.Token);
    }

    private void Progressed(long rootId, ScanProgress progress)
    {
        if (_firstScans.TryGetValue(rootId, out var scan))
        {
            _firstScans[rootId] = scan with { Progress = progress };
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void EndFirstScan(long rootId)
    {
        if (_firstScans.Remove(rootId))
        {
            FirstScanEnded?.Invoke(this, rootId);
        }
    }

    public void StopWatching(long rootId)
    {
        if (_watches.Remove(rootId, out var stop))
        {
            stop.Cancel();
            stop.Dispose();
        }
        _indexing.Remove(rootId);
        EndFirstScan(rootId);
        Update();
    }

    public void StopAll()
    {
        foreach (var id in _watches.Keys.ToList())
        {
            StopWatching(id);
        }
    }

    private async Task WatchAsync(long rootId, Action<WatchUpdate> onUpdate, Action<ScanProgress>? progress, CancellationToken cancellation)
    {
        try
        {
            await Task.Run(() => scanner.WatchAsync(rootId, onUpdate, progress, cancellation), cancellation);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (error is RootUnavailableException or IOException or UnauthorizedAccessException)
        {
            Indexed(rootId, isOnline: false);
        }
    }

    private void Indexed(long rootId, bool isOnline)
    {
        _indexing.Remove(rootId);
        EndFirstScan(rootId);
        if (isOnline)
        {
            OfflineRoots.Remove(rootId);
            RequestMetadata();
        }
        else
        {
            OfflineRoots.Add(rootId);
        }
        Update();
    }

    /// <summary>Runs stage 2 until nothing is pending; requests during a run trigger one more pass.</summary>
    private void RequestMetadata()
    {
        _metadataRequested = true;
        if (_metadata is not null)
        {
            return;
        }
        _metadata = RunMetadataAsync();
        Update();
    }

    private async Task RunMetadataAsync()
    {
        while (_metadataRequested)
        {
            _metadataRequested = false;
            await Task.Run(() => scanner.ExtractMetadataAsync());
        }
        _metadata = null;
        Update();
    }

    private void Update()
    {
        Activity = _indexing.Count > 0 ? LibraryActivity.Indexing : _metadata is not null ? LibraryActivity.ReadingDetails : LibraryActivity.None;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
