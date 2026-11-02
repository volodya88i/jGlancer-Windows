using JGlancer.Core.Model;
using JGlancer.Core.Tests.Support;
using JGlancer.Media.Thumbnails;

namespace JGlancer.Media.Tests;

/// <summary>Counts generations; optionally slow, to observe merging and cancellation.</summary>
internal sealed class CountingGenerator(int delayMilliseconds = 0, bool produces = true) : IThumbnailGenerator
{
    private int _calls;
    private int _cancellations;

    public int Calls => Volatile.Read(ref _calls);

    public int Cancellations => Volatile.Read(ref _cancellations);

    public ThumbnailImage? Generate(ThumbnailRequest request, int maxPixelSize, CancellationToken cancellation)
    {
        Interlocked.Increment(ref _calls);
        if (cancellation.WaitHandle.WaitOne(delayMilliseconds))
        {
            Interlocked.Increment(ref _cancellations);
            return null;
        }
        return produces ? Image(maxPixelSize / 2, maxPixelSize / 4) : null;
    }

    public static ThumbnailImage Image(int width, int height, byte alpha = 255)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(40 * alpha / 255);
            pixels[i + 1] = (byte)(120 * alpha / 255);
            pixels[i + 2] = (byte)(200 * alpha / 255);
            pixels[i + 3] = alpha;
        }
        return new ThumbnailImage(width, height, pixels);
    }
}

public sealed class ThumbnailServiceTests
{
    private static ThumbnailRequest Request(long id, ThumbnailBucket bucket = ThumbnailBucket.Medium, double modified = 0) =>
        new(id, $@"C:\nonexistent\{id}.jpg", MediaKind.Photo, null, DateTimeOffset.FromUnixTimeSeconds((long)modified), null, bucket);

    [Fact]
    public async Task ConcurrentRequestsShareOneGeneration()
    {
        var generator = new CountingGenerator(delayMilliseconds: 100);
        using var disk = ThumbnailDiskCache.InMemory();
        var service = new ThumbnailService(disk, generator);
        var images = await Task.WhenAll(service.GetAsync(Request(1), ThumbnailPriority.Visible), service.GetAsync(Request(1), ThumbnailPriority.Nearby));
        Assert.All(images, image => Assert.Equal(160, image?.Width));
        Assert.Equal(1, generator.Calls);
    }

    [Fact]
    public async Task TheDiskServesRepeatsAcrossServices()
    {
        using var disk = ThumbnailDiskCache.InMemory();
        var generator = new CountingGenerator();
        Assert.Equal(160, (await new ThumbnailService(disk, generator).GetAsync(Request(1), ThumbnailPriority.Visible))?.Width);
        Assert.Equal(160, (await new ThumbnailService(disk, generator).GetAsync(Request(1), ThumbnailPriority.Visible))?.Width);
        Assert.Equal(1, generator.Calls);
    }

    [Fact]
    public async Task MissingThumbnailsAreRememberedAsNone()
    {
        var generator = new CountingGenerator(produces: false);
        using var disk = ThumbnailDiskCache.InMemory();
        Assert.Null(await new ThumbnailService(disk, generator).GetAsync(Request(1), ThumbnailPriority.Visible));
        Assert.Equal([], disk.Data(Request(1).Key)!);
        Assert.Null(await new ThumbnailService(disk, generator).GetAsync(Request(1), ThumbnailPriority.Visible));
        Assert.Equal(1, generator.Calls);
    }

    [Fact]
    public async Task SmallRequestsReuseMediumAndModifiedFilesRegenerate()
    {
        var generator = new CountingGenerator();
        using var disk = ThumbnailDiskCache.InMemory();
        var service = new ThumbnailService(disk, generator);
        await service.GetAsync(Request(1, ThumbnailBucket.Medium), ThumbnailPriority.Visible);
        await service.GetAsync(Request(1, ThumbnailBucket.Small), ThumbnailPriority.Visible);
        Assert.Equal(1, generator.Calls);
        await service.GetAsync(Request(1, ThumbnailBucket.Large), ThumbnailPriority.Visible);
        await service.GetAsync(Request(1, ThumbnailBucket.Medium, modified: 60), ThumbnailPriority.Visible);
        Assert.Equal(3, generator.Calls);
    }

    [Fact]
    public async Task CancellingTheOnlyWaiterCancelsTheGeneration()
    {
        var generator = new CountingGenerator(delayMilliseconds: 5000);
        using var disk = ThumbnailDiskCache.InMemory();
        var service = new ThumbnailService(disk, generator);
        using var cancel = new CancellationTokenSource();
        var waiting = service.GetAsync(Request(1), ThumbnailPriority.Visible, cancel.Token);
        await Task.Delay(100);
        await cancel.CancelAsync();
        Assert.Null(await waiting);
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (generator.Cancellations == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
        Assert.Equal(1, generator.Cancellations);
        // Nothing half-made was stored.
        Assert.Null(disk.Data(Request(1).Key));
    }

    [Fact]
    public async Task OneOfTwoWaitersLeavingKeepsTheJob()
    {
        var generator = new CountingGenerator(delayMilliseconds: 200);
        using var disk = ThumbnailDiskCache.InMemory();
        var service = new ThumbnailService(disk, generator);
        using var cancel = new CancellationTokenSource();
        var leaving = service.GetAsync(Request(1), ThumbnailPriority.Nearby, cancel.Token);
        var staying = service.GetAsync(Request(1), ThumbnailPriority.Visible);
        await cancel.CancelAsync();
        Assert.Null(await leaving);
        Assert.NotNull(await staying);
        Assert.Equal(0, generator.Cancellations);
    }

    [Fact]
    public void BucketsCoverRequestedSizes()
    {
        Assert.Equal(ThumbnailBucket.Small, ThumbnailBuckets.Covering(120));
        Assert.Equal(ThumbnailBucket.Medium, ThumbnailBuckets.Covering(161));
        Assert.Equal(ThumbnailBucket.Large, ThumbnailBuckets.Covering(5000));
        Assert.Equal(ThumbnailBucket.Medium, Request(1, ThumbnailBucket.Small).Key.Bucket);
    }
}

public sealed class ThumbnailDiskCacheTests
{
    private static ThumbnailKey Key(long id, long mtime = 1) => new(id, mtime, ThumbnailBucket.Medium);

    [Fact]
    public void StoresFindsAndMarksMissingThumbnails()
    {
        using var cache = ThumbnailDiskCache.InMemory();
        Assert.Null(cache.Data(Key(1)));
        cache.Store([1, 2, 3], 10, 10, Key(1));
        cache.Store([], 0, 0, Key(2));
        Assert.Equal([1, 2, 3], cache.Data(Key(1))!);
        Assert.Equal([], cache.Data(Key(2))!);
        Assert.Null(cache.Data(Key(1, mtime: 2)));
        Assert.Equal(3, cache.UsedBytes());
        cache.Store([9], 1, 1, Key(1));
        Assert.Equal(1, cache.UsedBytes());
        cache.RemoveAll();
        Assert.Equal(0, cache.UsedBytes());
    }

    [Fact]
    public void EvictionDropsLeastRecentlyUsedEntriesFirst()
    {
        using var cache = ThumbnailDiskCache.InMemory(limitBytes: 1000);
        var start = DateTimeOffset.FromUnixTimeSeconds(1_000_000);
        for (var id = 1; id <= 10; id++)
        {
            cache.Store(new byte[150], 1, 1, Key(id), start.AddSeconds(id));
        }
        // Entry 1 is read two days later, so it becomes the most recently used.
        cache.Data(Key(1), start.AddDays(2));
        cache.EvictIfNeeded();
        Assert.True(cache.UsedBytes() <= 900);
        Assert.NotNull(cache.Data(Key(1)));
        Assert.Null(cache.Data(Key(2)));
        Assert.NotNull(cache.Data(Key(10)));
    }

    [Fact]
    public void ASmallerCapEvictsAtOnce()
    {
        using var cache = ThumbnailDiskCache.InMemory(limitBytes: 10_000);
        for (var id = 0; id < 10; id++)
        {
            cache.Store(Enumerable.Repeat((byte)1, 900).ToArray(), 1, 1, Key(id, 0));
        }
        Assert.Equal(9000, cache.UsedBytes());
        cache.SetLimit(3000);
        Assert.Equal(3000, cache.LimitBytes);
        Assert.True(cache.UsedBytes() <= 2700);
    }

    [Fact]
    public void CorruptCacheFileIsRecreated()
    {
        using var folder = new TemporaryDirectory();
        var path = folder.Write("cache.sqlite", "this is not a database");
        using var cache = ThumbnailDiskCache.Open(path);
        cache.Store([1], 1, 1, Key(1));
        Assert.Equal([1], cache.Data(Key(1))!);
    }
}

public sealed class CodecAndLimiterTests
{
    [Fact]
    public void JpegRoundTripsAndPngKeepsTransparency()
    {
        var opaque = CountingGenerator.Image(64, 32);
        var jpeg = ThumbnailCodec.Encode(opaque, preserveAlpha: false);
        Assert.Equal([0xFF, 0xD8], jpeg[..2]);
        var decoded = ThumbnailCodec.Decode(jpeg)!;
        Assert.Equal((64, 32), (decoded.Width, decoded.Height));
        Assert.InRange(decoded.Pixels[2], 190, 210); // red channel of a BGRA pixel, JPEG-close

        var translucent = CountingGenerator.Image(8, 8, alpha: 128);
        var png = ThumbnailCodec.Encode(translucent, preserveAlpha: true);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
        Assert.InRange(ThumbnailCodec.Decode(png)!.Pixels[3], 127, 129);
        // Transparency of a photo type is not kept: JPEG.
        Assert.Equal([0xFF, 0xD8], ThumbnailCodec.Encode(translucent, preserveAlpha: false)[..2]);
        Assert.Null(ThumbnailCodec.Decode([1, 2, 3]));
    }

    [Fact]
    public async Task CapacityIsRespectedAndHighPriorityGoesFirst()
    {
        var limiter = new PriorityLimiter(1);
        Assert.True(await limiter.AcquireAsync(ThumbnailPriority.Visible, null, default));
        var order = new List<string>();
        var background = Task.Run(async () =>
        {
            if (await limiter.AcquireAsync(ThumbnailPriority.Background, null, default))
            {
                lock (order) { order.Add("background"); }
                limiter.Release();
            }
        });
        await Until(() => limiter.Waiting == 1);
        var visible = Task.Run(async () =>
        {
            if (await limiter.AcquireAsync(ThumbnailPriority.Visible, null, default))
            {
                lock (order) { order.Add("visible"); }
                limiter.Release();
            }
        });
        await Until(() => limiter.Waiting == 2);
        Assert.Equal(1, limiter.Running);
        limiter.Release();
        await Task.WhenAll(background, visible);
        Assert.Equal(["visible", "background"], order);
    }

    [Fact]
    public async Task ADemotedWaiterWaitsBehindTheRest()
    {
        var limiter = new PriorityLimiter(1);
        Assert.True(await limiter.AcquireAsync(ThumbnailPriority.Visible, null, default));
        var order = new List<string>();
        Task Wait(string name, ThumbnailPriority priority) => Task.Run(async () =>
        {
            if (await limiter.AcquireAsync(priority, name, default))
            {
                lock (order) { order.Add(name); }
                limiter.Release();
            }
        });
        var scrolledAway = Wait("scrolled away", ThumbnailPriority.Visible);
        await Until(() => limiter.Waiting == 1);
        var nearby = Wait("nearby", ThumbnailPriority.Nearby);
        await Until(() => limiter.Waiting == 2);
        // The tile left the screen: it now waits behind the prefetch instead of going first.
        limiter.SetPriority("scrolled away", ThumbnailPriority.Background);
        limiter.Release();
        await Task.WhenAll(scrolledAway, nearby);
        Assert.Equal(["nearby", "scrolled away"], order);
    }

    [Fact]
    public async Task BoostedWaiterOvertakesAndCancelledWaiterGetsNoSlot()
    {
        var limiter = new PriorityLimiter(1);
        Assert.True(await limiter.AcquireAsync(ThumbnailPriority.Visible, null, default));
        var order = new List<string>();
        var nearby = Task.Run(async () =>
        {
            if (await limiter.AcquireAsync(ThumbnailPriority.Nearby, null, default))
            {
                lock (order) { order.Add("nearby"); }
                limiter.Release();
            }
        });
        await Until(() => limiter.Waiting == 1);
        var boosted = Task.Run(async () =>
        {
            if (await limiter.AcquireAsync(ThumbnailPriority.Background, "x", default))
            {
                lock (order) { order.Add("boosted"); }
                limiter.Release();
            }
        });
        using var cancel = new CancellationTokenSource();
        var cancelled = limiter.AcquireAsync(ThumbnailPriority.Visible, null, cancel.Token);
        await Until(() => limiter.Waiting == 3);
        limiter.Boost("x", ThumbnailPriority.Visible);
        await cancel.CancelAsync();
        Assert.False(await cancelled);
        limiter.Release();
        await Task.WhenAll(nearby, boosted);
        Assert.Equal(["boosted", "nearby"], order);
        Assert.Equal(0, limiter.Running);
    }

    /// <summary>Waits until <paramref name="condition"/> holds (waiters queue up on the thread pool, which may be slow
    /// to start one on a busy machine); fails after 10 s.</summary>
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting");
            await Task.Delay(5);
        }
    }
}
