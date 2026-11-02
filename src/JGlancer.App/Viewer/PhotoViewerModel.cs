using System.Diagnostics;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using JGlancer.App.Library;
using JGlancer.App.Services;
using JGlancer.Core.Model;
using JGlancer.Media.Photos;
using JGlancer.Media.Thumbnails;

namespace JGlancer.App.Viewer;

/// <summary>A decoded picture of one photo, with the photo's full pixel size (orientation applied).</summary>
/// <param name="IsPlaceholder">The grid's thumbnail standing in until the screen-size decode arrives (not owned).</param>
internal sealed record ViewerPicture(Bitmap Bitmap, int FullWidth, int FullHeight, bool IsFullResolution, bool IsPlaceholder);

/// <summary>
/// The photo viewer's state (macOS PhotoViewerModel, ARCHITECTURE §8): the photos of the folder, the current index, a
/// pager that keeps the current photo and ±2 neighbors decoded at screen size, and full resolution for the current one
/// when zoomed past fit. Lives on the UI thread; decoding runs on the pool and is cancelled when it leaves the window.
/// </summary>
internal sealed class PhotoViewerModel : IFilmstripSource, IDisposable
{
    /// <summary>Screen-size decodes running at once (the current photo is always requested first).</summary>
    private const int DecodeSlots = 2;

    private readonly AppServices _services;
    private readonly Avalonia.PixelSize _screen;
    private readonly List<long> _entries;
    private readonly Dictionary<long, Asset> _rows = [];
    private readonly HashSet<long> _requestedRows = [];
    private readonly Dictionary<long, ViewerPicture> _decoded = [];
    private readonly Dictionary<long, CancellationTokenSource> _decodes = [];
    private readonly SemaphoreSlim _decodeSlots = new(DecodeSlots);
    private CancellationTokenSource? _fullDecode;
    private ViewerPicture? _picture;
    private long? _navigationStart;
    private bool _disposed;

    /// <param name="screen">The largest screen in device pixels: screen-size decodes fit it.</param>
    /// <param name="opened">
    /// The opened photo's row when the caller has it: the grid's thumbnail shows in the first frame and the decode starts
    /// at once, instead of after the rows load (a black stage for 100–150 ms).
    /// </param>
    public PhotoViewerModel(AppServices services, IReadOnlyList<long> entries, int index, Avalonia.PixelSize screen, Asset? opened = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(entries);
        _services = services;
        _entries = [.. entries];
        _screen = screen;
        Index = Pager.Move(index, 0, _entries.Count);
        if (opened is not null)
        {
            _rows[opened.Id] = opened;
        }
        Refresh();
    }

    /// <summary>Raised when the index, the shown picture, the rotation or the failure state changed.</summary>
    public event EventHandler? Changed;

    public int Index { get; private set; }

    public int Count => _entries.Count;

    public long? CurrentId => Index < _entries.Count ? _entries[Index] : null;

    /// <summary>The current photo's row, once loaded.</summary>
    public Asset? Current => CurrentId is { } id && _rows.TryGetValue(id, out var row) ? row : null;

    /// <summary>The best picture available for the current photo (null while nothing is ready).</summary>
    public ViewerPicture? Picture => _picture;

    /// <summary>Quarter turns clockwise for viewing only (the file is never changed); back to 0 on the next photo.</summary>
    public int QuarterTurns { get; private set; }

    /// <summary>True when the current photo could not be decoded.</summary>
    public bool Failed { get; private set; }

    /// <summary>Milliseconds from the last navigation to its picture being shown (benchmarks, macOS viewer.next).</summary>
    public double? LastNavigationMilliseconds { get; private set; }

    /// <summary>Thumbnails for the filmstrip (the library's, shared with the grid).</summary>
    public ThumbnailCache Bitmaps => _services.Bitmaps;

    public ThumbnailService Thumbnails => _services.Thumbnails;

    // MARK: Navigation

    public bool CanMove(int offset) => Index + offset >= 0 && Index + offset < _entries.Count;

    public void Move(int offset)
    {
        var target = Pager.Move(Index, offset, _entries.Count);
        if (target == Index)
        {
            return;
        }
        _navigationStart = Stopwatch.GetTimestamp();
        LastNavigationMilliseconds = null;
        Index = target;
        QuarterTurns = 0;
        Refresh();
    }

    public void MoveTo(int index) => Move(index - Index);

    /// <summary>
    /// The current photo went to the Recycle Bin: the next one shows (the previous one at the end). False when it was
    /// the last photo (the viewer closes).
    /// </summary>
    public bool RemoveCurrent()
    {
        if (CurrentId is not { } removed)
        {
            return false;
        }
        _entries.RemoveAt(Index);
        _rows.Remove(removed);
        if (_decoded.Remove(removed, out var picture))
        {
            Release(picture);
        }
        if (_entries.Count == 0)
        {
            return false;
        }
        Index = Math.Min(Index, _entries.Count - 1);
        QuarterTurns = 0;
        Refresh();
        return true;
    }

    /// <summary>Turns the view by quarter turns (positive: clockwise), for viewing only.</summary>
    public void Rotate(int turns)
    {
        QuarterTurns = ((QuarterTurns + turns) % 4 + 4) % 4;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The decoded picture of the photo <paramref name="offset"/> places away (swipe animation), if ready.</summary>
    public ViewerPicture? NeighborPicture(int offset)
    {
        var position = Index + offset;
        return position >= 0 && position < _entries.Count && _decoded.TryGetValue(_entries[position], out var picture) ? picture : null;
    }

    // MARK: Pager

    /// <summary>Shows the best available picture for the current photo and keeps ±2 neighbors decoding.</summary>
    private void Refresh()
    {
        Failed = false;
        CancelFullDecode();
        var window = Pager.Window(Index, _entries.Count).Select(i => _entries[i]).ToList();
        var keep = window.ToHashSet();
        foreach (var (id, cancel) in _decodes.Where(pair => !keep.Contains(pair.Key)).ToList())
        {
            cancel.Cancel();
            _decodes.Remove(id);
        }
        foreach (var (id, picture) in _decoded.Where(pair => !keep.Contains(pair.Key)).ToList())
        {
            _decoded.Remove(id);
            Release(picture);
        }
        LoadRows(window);
        if (CurrentId is { } current && _decoded.TryGetValue(current, out var ready))
        {
            Show(ready);
        }
        else
        {
            SetPicture(Placeholder());
        }
        for (var order = 0; order < window.Count; order++)
        {
            var id = window[order];
            if (!_decoded.ContainsKey(id) && !_decodes.ContainsKey(id) && _rows.ContainsKey(id))
            {
                Decode(id);
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Show(ViewerPicture picture)
    {
        SetPicture(picture);
        if (_navigationStart is { } start)
        {
            LastNavigationMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _navigationStart = null;
        }
    }

    private void SetPicture(ViewerPicture? picture)
    {
        var previous = _picture;
        _picture = picture;
        // A full-resolution picture replaced by the next photo is no longer held anywhere.
        if (previous is not null && previous != picture && !previous.IsPlaceholder && !_decoded.ContainsValue(previous))
        {
            previous.Bitmap.Dispose();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The grid's cached thumbnail, shown scaled up until the screen-size decode arrives.</summary>
    private ViewerPicture? Placeholder()
    {
        if (Current is not { } asset)
        {
            return null;
        }
        foreach (var bucket in new[] { ThumbnailBucket.Large, ThumbnailBucket.Medium, ThumbnailBucket.Small })
        {
            if (_services.Bitmaps.TryGet(ThumbnailRequest.For(asset, bucket).Key, out var bitmap) && bitmap is not null)
            {
                var size = bitmap.PixelSize;
                return new ViewerPicture(bitmap, size.Width, size.Height, IsFullResolution: false, IsPlaceholder: true);
            }
        }
        return null;
    }

    private void Decode(long id)
    {
        if (!_rows.TryGetValue(id, out var asset))
        {
            return;
        }
        var cancel = new CancellationTokenSource();
        _decodes[id] = cancel;
        var decoder = _services.Photos;
        var screen = _screen;
        _ = Run();

        async Task Run()
        {
            // At most DecodeSlots decodes at a time: a WIC decode cannot stop halfway, so while → is held the
            // photos skipped over are cancelled while they still wait, instead of all decoding in parallel.
            try
            {
                await _decodeSlots.WaitAsync(cancel.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            ViewerPicture? picture;
            // Background metadata reading waits while photos decode: the disk is the viewer's.
            var pause = _services.Scanner.PauseMetadata();
            try
            {
                picture = await Task.Run(() => DecodePicture(decoder, asset.Path, screen.Width, screen.Height, cancel.Token), cancel.Token)
                    .ContinueWith(task => task.IsCompletedSuccessfully ? task.Result : null, TaskScheduler.Default);
            }
            finally
            {
                pause.Dispose();
                _decodeSlots.Release();
            }
            if (cancel.IsCancellationRequested || _disposed)
            {
                picture?.Bitmap.Dispose();
                return;
            }
            _decodes.Remove(id);
            var isShown = CurrentId == id;
            if (picture is not null)
            {
                _decoded[id] = picture;
                if (isShown)
                {
                    Show(picture);
                }
            }
            else if (isShown)
            {
                Failed = true;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Decodes straight into a bitmap's own pixels (W12): no managed buffer of the photo's size and no copy, so paging
    /// through big photos leaves the garbage collector nothing to sweep.
    /// </summary>
    internal static ViewerPicture? DecodePicture(IPhotoDecoder decoder, string path, int boxWidth, int boxHeight, CancellationToken cancellation)
    {
        WriteableBitmap? bitmap = null;
        ILockedFramebuffer? locked = null;
        try
        {
            var full = decoder.DecodeInto(path, boxWidth, boxHeight, (width, height) =>
            {
                bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                locked = bitmap.Lock();
                return new PixelTarget(locked.Address, locked.RowBytes);
            }, cancellation);
            locked?.Dispose();
            locked = null;
            if (full is not { } size || bitmap is null)
            {
                bitmap?.Dispose();
                return null;
            }
            var pixels = bitmap.PixelSize;
            return new ViewerPicture(bitmap, size.FullWidth, size.FullHeight,
                IsFullResolution: pixels.Width >= size.FullWidth && pixels.Height >= size.FullHeight, IsPlaceholder: false);
        }
        catch
        {
            locked?.Dispose();
            bitmap?.Dispose();
            throw;
        }
    }

    /// <summary>Decodes the current photo at full resolution (called when zoomed beyond fit).</summary>
    public void RequestFullResolution()
    {
        if (_fullDecode is not null || _picture is not { IsFullResolution: false, IsPlaceholder: false } || Current is not { } asset)
        {
            return;
        }
        var cancel = new CancellationTokenSource();
        _fullDecode = cancel;
        var id = asset.Id;
        var decoder = _services.Photos;
        _ = Run();

        async Task Run()
        {
            var picture = await Task.Run(() => DecodePicture(decoder, asset.Path, IPhotoDecoder.Unbounded, IPhotoDecoder.Unbounded, cancel.Token), cancel.Token)
                .ContinueWith(task => task.IsCompletedSuccessfully ? task.Result : null, TaskScheduler.Default);
            if (cancel.IsCancellationRequested || _disposed || CurrentId != id || picture is null)
            {
                picture?.Bitmap.Dispose();
                return;
            }
            SetPicture(picture);
        }
    }

    private void CancelFullDecode()
    {
        _fullDecode?.Cancel();
        _fullDecode = null;
    }

    private void Release(ViewerPicture picture)
    {
        if (!picture.IsPlaceholder && picture != _picture)
        {
            picture.Bitmap.Dispose();
        }
    }

    // MARK: Rows

    /// <summary>The filmstrip tile at <paramref name="position"/>, or null while its row loads.</summary>
    public FilmstripItem? Item(int position, ThumbnailBucket bucket) => Row(position) is { } asset
        ? new FilmstripItem(asset.Name, asset.Kind, ThumbnailRequest.For(asset, bucket))
        : null;

    /// <summary>The row of the photo at <paramref name="position"/>, or null while it loads.</summary>
    public Asset? Row(int position) =>
        position >= 0 && position < _entries.Count && _rows.TryGetValue(_entries[position], out var row) ? row : null;

    /// <summary>Loads library rows around <paramref name="position"/> in one query (filmstrip tiles coming on screen).</summary>
    public void PrepareRows(int position)
    {
        if (position < 0 || position >= _entries.Count)
        {
            return;
        }
        var from = Math.Max(0, position - 20);
        var to = Math.Min(_entries.Count, position + 21);
        LoadRows(_entries.GetRange(from, to - from));
    }

    /// <summary>Loads the rows the pager needs (plus ±10 around the current photo), then continues decoding.</summary>
    private void LoadRows(IReadOnlyList<long> window)
    {
        var missing = window.Where(id => !_rows.ContainsKey(id) && !_requestedRows.Contains(id)).ToList();
        if (missing.Count == 0)
        {
            return;
        }
        var from = Math.Max(0, Index - 10);
        var to = Math.Min(_entries.Count, Index + 11);
        missing.AddRange(_entries.GetRange(from, to - from).Where(id => !_rows.ContainsKey(id) && !_requestedRows.Contains(id)));
        var ids = missing.Distinct().ToList();
        _requestedRows.UnionWith(ids);
        _ = Load();

        async Task Load()
        {
            var assets = await _services.AssetsAsync(ids);
            if (_disposed)
            {
                return;
            }
            foreach (var asset in assets)
            {
                _rows[asset.Id] = asset;
            }
            RowsLoaded?.Invoke(this, EventArgs.Empty);
            // Only rows the pager is waiting for restart it (filmstrip rows must not drop a full-size picture).
            var pagerWindow = Pager.Window(Index, _entries.Count).Select(i => _entries[i]).ToHashSet();
            if (assets.Any(asset => pagerWindow.Contains(asset.Id)))
            {
                Refresh();
            }
        }
    }

    /// <summary>Raised when rows arrived (filmstrip names and thumbnails).</summary>
    public event EventHandler? RowsLoaded;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        CancelFullDecode();
        foreach (var cancel in _decodes.Values)
        {
            cancel.Cancel();
        }
        _decodes.Clear();
        foreach (var picture in _decoded.Values.Where(p => !p.IsPlaceholder))
        {
            picture.Bitmap.Dispose();
        }
        if (_picture is { IsPlaceholder: false } shown && !_decoded.ContainsValue(shown))
        {
            shown.Bitmap.Dispose();
        }
        _decoded.Clear();
        _picture = null;
    }
}
