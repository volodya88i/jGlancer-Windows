namespace JGlancer.Media.Thumbnails;

/// <summary>
/// Produces thumbnails for the grid, filmstrip and previews (macOS ThumbnailService): <c>cache.sqlite</c> first, else
/// generation from the original under a priority-aware concurrency limit, then stored. Identical concurrent requests
/// share one job; a job is cancelled when every caller waiting for it has given up (tiles scrolled away). The memory
/// cache of display-ready bitmaps belongs to the UI layer.
/// </summary>
public sealed class ThumbnailService
{
    private readonly ThumbnailDiskCache? _disk;
    private readonly IThumbnailGenerator _generator;
    private readonly PriorityLimiter _limiter;
    private readonly Lock _lock = new();
    private readonly Dictionary<ThumbnailKey, Job> _jobs = [];

    /// <param name="concurrency">
    /// Generations running at once: by default a quarter of the logical CPUs, 2 to 4. A HEIC or large PNG is a full
    /// decode, and some decoders use several cores each; measured on a real Downloads folder (W-37), more at once only
    /// made each slower and the memory higher.
    /// </param>
    public ThumbnailService(ThumbnailDiskCache? disk, IThumbnailGenerator generator, int concurrency = 0)
    {
        _disk = disk;
        _generator = generator;
        _limiter = new PriorityLimiter(concurrency > 0 ? concurrency : DefaultConcurrency);
    }

    /// <summary>
    /// The thumbnail for <paramref name="request"/>, or null if the file has none or <paramref name="cancellation"/>
    /// fired. Never throws for unreadable files.
    /// </summary>
    public async Task<ThumbnailImage?> GetAsync(ThumbnailRequest request, ThumbnailPriority priority, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = request.Key;
        Job job;
        lock (_lock)
        {
            if (_jobs.TryGetValue(key, out var existing))
            {
                job = existing;
                _limiter.Boost(key, priority);
            }
            else
            {
                job = new Job();
                _jobs[key] = job;
                job.Task = Task.Run(() => RunAsync(request, priority, job.Cancellation.Token));
            }
            job.Waiters++;
        }
        try
        {
            return await job.Task.WaitAsync(cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            Leave(key, job);
        }
    }

    private readonly long[] _generated = new long[3];
    private readonly long[] _generationTicks = new long[3];
    private long _diskHits;

    /// <summary>What the service did so far (benchmarks): generations and their time per kind, disk cache hits.</summary>
    public (long Photos, long Videos, long Audio, double PhotoMs, double VideoMs, double AudioMs, long DiskHits) Statistics =>
        (Interlocked.Read(ref _generated[(int)Core.Model.MediaKind.Photo]),
         Interlocked.Read(ref _generated[(int)Core.Model.MediaKind.Video]),
         Interlocked.Read(ref _generated[(int)Core.Model.MediaKind.Audio]),
         TimeSpan.FromTicks(Interlocked.Read(ref _generationTicks[(int)Core.Model.MediaKind.Photo])).TotalMilliseconds,
         TimeSpan.FromTicks(Interlocked.Read(ref _generationTicks[(int)Core.Model.MediaKind.Video])).TotalMilliseconds,
         TimeSpan.FromTicks(Interlocked.Read(ref _generationTicks[(int)Core.Model.MediaKind.Audio])).TotalMilliseconds,
         Interlocked.Read(ref _diskHits));

    /// <summary>Generations at once by default (see the constructor).</summary>
    public static int DefaultConcurrency => Math.Clamp(Environment.ProcessorCount / 4, 2, 4);

    /// <summary>
    /// Moves a queued generation to <paramref name="priority"/>, up or down (a tile that scrolled away waits behind what
    /// is on screen instead of being thrown away). No effect once it runs.
    /// </summary>
    public void SetPriority(ThumbnailKey key, ThumbnailPriority priority) => _limiter.SetPriority(key, priority);

    /// <summary>Removes every stored thumbnail ("Clear Cache").</summary>
    public void RemoveAll() => _disk?.RemoveAll();

    private void Leave(ThumbnailKey key, Job job)
    {
        lock (_lock)
        {
            if (--job.Waiters > 0)
            {
                return;
            }
            if (_jobs.TryGetValue(key, out var current) && ReferenceEquals(current, job))
            {
                _jobs.Remove(key);
            }
        }
        if (!job.Task.IsCompleted)
        {
            job.Cancellation.Cancel();
        }
    }

    private async Task<ThumbnailImage?> RunAsync(ThumbnailRequest request, ThumbnailPriority priority, CancellationToken cancellation)
    {
        var key = request.Key;
        // Files outside the library (negative stand-in ids, macOS D-63) are never written to or read from the disk cache.
        var disk = key.AssetId > 0 ? _disk : null;
        if (disk?.Data(key) is { } stored)
        {
            Interlocked.Increment(ref _diskHits);
            return stored.Length == 0 ? null : ThumbnailCodec.Decode(stored);
        }
        if (!await _limiter.AcquireAsync(priority, key, cancellation).ConfigureAwait(false))
        {
            return null;
        }
        ThumbnailImage? image;
        try
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            image = BelowNormal(() => _generator.Generate(request, key.Bucket.MaxPixelSize(), cancellation));
            var kind = (int)request.Kind;
            if (kind is >= 0 and < 3)
            {
                Interlocked.Increment(ref _generated[kind]);
                Interlocked.Add(ref _generationTicks[kind], System.Diagnostics.Stopwatch.GetElapsedTime(started).Ticks);
            }
        }
        finally
        {
            _limiter.Release();
        }
        if (cancellation.IsCancellationRequested)
        {
            return null;
        }
        var encoded = image is null ? [] : ThumbnailCodec.Encode(image, ThumbnailCodec.PreservesAlpha(request.TypeIdentifier));
        disk?.Store(encoded, image?.Width ?? 0, image?.Height ?? 0, key);
        return image;
    }

    /// <summary>
    /// Runs background work on this pool thread at below-normal priority, so the UI thread, the renderer and video
    /// playback always win the CPU; the priority is restored before the thread goes back to the pool.
    /// </summary>
    internal static T BelowNormal<T>(Func<T> work)
    {
        var thread = Thread.CurrentThread;
        var previous = thread.Priority;
        thread.Priority = ThreadPriority.BelowNormal;
        try
        {
            return work();
        }
        finally
        {
            thread.Priority = previous;
        }
    }

    private sealed class Job
    {
        public Task<ThumbnailImage?> Task { get; set; } = System.Threading.Tasks.Task.FromResult<ThumbnailImage?>(null);
        public CancellationTokenSource Cancellation { get; } = new();
        public int Waiters { get; set; }
    }
}
