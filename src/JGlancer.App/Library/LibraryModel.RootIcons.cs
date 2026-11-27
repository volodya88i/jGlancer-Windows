using Avalonia.Media.Imaging;
using JGlancer.Core.Model;
using JGlancer.Core.Playback;
using JGlancer.Media.Icons;
using JGlancer.Media.Thumbnails;

namespace JGlancer.App.Library;

/// <summary>
/// Library folders' own icons (macOS D-129, MAC-052): kept in step with the library, pictures decoded once off the UI
/// thread; set from the picker (a symbol, an emoji, a picture file) or from a photo of the library (Use as Folder Icon).
/// </summary>
internal sealed partial class LibraryModel
{
    private IReadOnlyDictionary<long, FolderIconImage> _rootIcons = new Dictionary<long, FolderIconImage>();
    private CancellationTokenSource? _rootIconsWatch;

    /// <summary>The icons of the library folders that have one, by root id.</summary>
    public IReadOnlyDictionary<long, FolderIconImage> RootIcons => _rootIcons;

    /// <summary>A library folder's icon changed.</summary>
    public event EventHandler? RootIconsChanged;

    private void StartRootIcons()
    {
        _rootIconsWatch?.Cancel();
        var stop = new CancellationTokenSource();
        _rootIconsWatch = stop;
        _ = WatchRootIconsAsync(stop.Token);
    }

    private async Task WatchRootIconsAsync(CancellationToken cancellation)
    {
        try
        {
            await foreach (var icons in Store.ObserveRootIcons(cancellation))
            {
                var images = await Task.Run(() => Images(icons), cancellation);
                _rootIcons = images;
                RootIconsChanged?.Invoke(this, EventArgs.Empty);
                TreesChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static Dictionary<long, FolderIconImage> Images(IReadOnlyDictionary<long, FolderIcon> icons)
    {
        var images = new Dictionary<long, FolderIconImage>();
        foreach (var (id, icon) in icons)
        {
            switch (icon)
            {
                case FolderIcon.Symbol symbol when FolderIconSymbols.Data(symbol.Name) is { } data:
                    images[id] = new FolderIconImage(data, null, null);
                    break;
                case FolderIcon.Emoji emoji:
                    images[id] = new FolderIconImage(null, emoji.Text, null);
                    break;
                case FolderIcon.Picture picture:
                    try
                    {
                        using var stream = new MemoryStream(picture.Jpeg);
                        images[id] = new FolderIconImage(null, null, new Bitmap(stream));
                    }
                    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
                    {
                        // An unreadable picture leaves the default icon.
                    }
                    break;
            }
        }
        return images;
    }

    /// <summary>Gives a library folder its own icon, or the default one back (<c>null</c>).</summary>
    public Task SetRootIconAsync(long rootId, FolderIcon? icon) => Store.SetRootIconAsync(rootId, icon);

    /// <summary>A picture file chosen on disk as the icon of a library folder, read at a small size; false when unreadable.</summary>
    public async Task<bool> SetRootIconFromFileAsync(long rootId, string path)
    {
        var jpeg = await Task.Run(() =>
        {
            var box = FolderIcon.PictureSide * 4;
            return Services.Photos.Decode(path, box, box, CancellationToken.None) is { } photo ? FolderIconPicture.Make(photo.Image) : null;
        });
        if (jpeg is null)
        {
            return false;
        }
        await Store.SetRootIconAsync(rootId, new FolderIcon.Picture(jpeg));
        return true;
    }

    /// <summary>
    /// A photo of the library as the icon of the library folder that holds it (Use as Folder Icon), made from its
    /// thumbnail; a photo kept only in the cloud is not downloaded for this (false then, as when it cannot be read).
    /// </summary>
    public async Task<bool> SetFolderIconFromAssetAsync(long assetId)
    {
        var assets = await Store.AssetsAsync([assetId]);
        if (assets.Count == 0 || assets[0] is not { Kind: MediaKind.Photo, IsCloudOnly: false } asset || (_primary ?? this).Folder(asset.FolderId) is not { } folder)
        {
            return false;
        }
        var request = Viewer.Media.MediaViewerModel.Thumbnail(PlaybackItem.From(asset), ThumbnailBucket.Medium);
        var thumbnail = await Thumbnails.GetAsync(request, ThumbnailPriority.Visible);
        var jpeg = thumbnail is null ? null : await Task.Run(() => FolderIconPicture.Make(thumbnail));
        if (jpeg is null)
        {
            return false;
        }
        await Store.SetRootIconAsync(folder.RootId, new FolderIcon.Picture(jpeg));
        return true;
    }
}
