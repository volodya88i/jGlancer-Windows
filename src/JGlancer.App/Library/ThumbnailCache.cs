using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using JGlancer.Media.Thumbnails;

namespace JGlancer.App.Library;

/// <summary>
/// Display-ready thumbnails in memory, bounded by decoded bytes (width × height × 4), least recently used out first
/// (macOS ThumbnailMemoryCache). Also remembers which assets have no thumbnail. UI thread only.
/// </summary>
internal sealed class ThumbnailCache(long limitBytes)
{
    private long _limitBytes = limitBytes;

    private const long NoneCost = 64;
    private readonly Dictionary<ThumbnailKey, LinkedListNode<Entry>> _map = [];
    private readonly LinkedList<Entry> _recent = new();
    private long _bytes;

    /// <summary>Decoded bytes held.</summary>
    public long Bytes => _bytes;

    /// <summary>True when the key is known: <paramref name="bitmap"/> is its thumbnail, or null for "has none".</summary>
    public bool TryGet(ThumbnailKey key, out Bitmap? bitmap)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _recent.Remove(node);
            _recent.AddFirst(node);
            bitmap = node.Value.Bitmap;
            return true;
        }
        bitmap = null;
        return false;
    }

    public bool Contains(ThumbnailKey key) => _map.ContainsKey(key);

    /// <summary>The byte limit, raised (never lowered) to what a large window needs to hold its screens (W-38).</summary>
    public long LimitBytes => _limitBytes;

    public void EnsureCapacity(long bytes) => _limitBytes = Math.Max(_limitBytes, bytes);

    public void Insert(ThumbnailKey key, Bitmap? bitmap)
    {
        if (_map.Remove(key, out var old))
        {
            _recent.Remove(old);
            _bytes -= old.Value.Cost;
            old.Value.Bitmap?.Dispose();
        }
        var entry = new Entry(key, bitmap, bitmap is null ? NoneCost : (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4);
        _map[key] = _recent.AddFirst(entry);
        _bytes += entry.Cost;
        while (_bytes > _limitBytes && _recent.Last is { } last && last != _map[key])
        {
            _recent.RemoveLast();
            _map.Remove(last.Value.Key);
            _bytes -= last.Value.Cost;
            // The renderer keeps its own reference to anything it is still drawing.
            last.Value.Bitmap?.Dispose();
        }
    }

    public void Clear()
    {
        foreach (var entry in _recent)
        {
            entry.Bitmap?.Dispose();
        }
        _recent.Clear();
        _map.Clear();
        _bytes = 0;
    }

    /// <summary>An Avalonia bitmap of the pixels (copied; safe to call off the UI thread).</summary>
    public static Bitmap ToBitmap(ThumbnailImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var handle = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, handle.AddrOfPinnedObject(),
                new PixelSize(image.Width, image.Height), new Vector(96, 96), image.Stride);
        }
        finally
        {
            handle.Free();
        }
    }

    private sealed record Entry(ThumbnailKey Key, Bitmap? Bitmap, long Cost);
}
