using JGlancer.App.Services;
using JGlancer.Core.Model;
using JGlancer.Media.Thumbnails;

namespace JGlancer.App.Viewer;

/// <summary>
/// Two photos side by side (W-77): each shows the grid's thumbnail at once, then its
/// screen-size decode, and its full resolution once zoomed past fit. Sides swap. UI thread; decodes on the pool.
/// </summary>
internal sealed class CompareModel : IDisposable
{
    private readonly AppServices _services;
    private readonly Avalonia.PixelSize _screen;
    private readonly Asset[] _assets;
    private readonly ViewerPicture?[] _pictures = new ViewerPicture?[2];
    private readonly bool[] _fullRequested = new bool[2];
    private readonly CancellationTokenSource _cancel = new();
    private bool _disposed;

    /// <param name="screen">The largest screen in device pixels: each side decodes to fit it (zooming stays sharp).</param>
    public CompareModel(AppServices services, Asset left, Asset right, Avalonia.PixelSize screen)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _screen = screen;
        _assets = [left, right];
        for (var side = 0; side < 2; side++)
        {
            _pictures[side] = Placeholder(_assets[side]);
            _ = DecodeAsync(_assets[side], full: false);
        }
    }

    /// <summary>A side's picture changed, or the sides swapped.</summary>
    public event EventHandler? Changed;

    public Asset Asset(int side) => _assets[side];

    public ViewerPicture? Picture(int side) => _pictures[side];

    /// <summary>The photo on each side changes places.</summary>
    public void Swap()
    {
        (_assets[0], _assets[1]) = (_assets[1], _assets[0]);
        (_pictures[0], _pictures[1]) = (_pictures[1], _pictures[0]);
        (_fullRequested[0], _fullRequested[1]) = (_fullRequested[1], _fullRequested[0]);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A side was zoomed past fit: its photo decodes at full resolution (once).</summary>
    public void RequestFullResolution(int side)
    {
        if (_fullRequested[side] || _pictures[side] is { IsFullResolution: true })
        {
            return;
        }
        _fullRequested[side] = true;
        _ = DecodeAsync(_assets[side], full: true);
    }

    private ViewerPicture? Placeholder(Asset asset)
    {
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

    private async Task DecodeAsync(Asset asset, bool full)
    {
        var decoder = _services.Photos;
        var box = full ? JGlancer.Media.Photos.IPhotoDecoder.Unbounded : Math.Max(_screen.Width, _screen.Height);
        var token = _cancel.Token;
        ViewerPicture? picture;
        try
        {
            picture = await Task.Run(() => PhotoViewerModel.DecodePicture(decoder, asset.Path, box, box, token), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        // The sides may have swapped meanwhile: the picture goes where its photo is now.
        var now = Array.IndexOf(_assets, asset);
        if (_disposed || picture is null || now < 0 || (!full && _pictures[now] is { IsPlaceholder: false }))
        {
            picture?.Bitmap.Dispose();
            return;
        }
        var old = _pictures[now];
        _pictures[now] = picture;
        Changed?.Invoke(this, EventArgs.Empty);
        // Once the canvas draws the new one.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Release(old), Avalonia.Threading.DispatcherPriority.Background);
    }

    private static void Release(ViewerPicture? picture)
    {
        if (picture is { IsPlaceholder: false })
        {
            picture.Bitmap.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _cancel.Cancel();
        _cancel.Dispose();
        foreach (var picture in _pictures)
        {
            Release(picture);
        }
    }
}
