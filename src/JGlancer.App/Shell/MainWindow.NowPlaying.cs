using JGlancer.App.Library;
using JGlancer.App.Viewer.Media;
using JGlancer.Core.Model;
using JGlancer.Core.Playback;

namespace JGlancer.App.Shell;

/// <summary>
/// The mini player (W-68): "Play in Mini Player" plays songs in the bar at the bottom of the window,
/// under the library and the viewers, until its × closes it. One song picked plays through the songs the pane shows
/// (as the player would page), several picked play in their order; Alt+double-click on a song plays it here.
/// Alt+"‹ Library" in the audio player closes it and its song goes on here from the same moment, with the player's
/// songs as the queue (macOS D-92); plain "‹ Library" stops it as before.
/// </summary>
internal sealed partial class MainWindow
{
    private NowPlaying? _nowPlaying;
    private Platform.Windows.MediaKeys? _mediaKeys;

    private void AttachNowPlaying(LibraryModel model)
    {
        _nowPlaying = new NowPlaying(model.Services);
        NowPlayingBar.Model = _nowPlaying;
        NowPlayingBar.FolderRequested += async (_, id) => await ShowInLibraryAsync(id);
        model.MiniPlayerRequested += (_, id) => _ = PlayInMiniPlayerAsync([id]);
        _nowPlaying.Changed += (_, _) => UpdateMediaKeys();
        // Developer runs leave the media keys to other players.
        if (!Developer.DeveloperOptions.Current.IsEphemeral)
        {
            _mediaKeys = new Platform.Windows.MediaKeys();
            _mediaKeys.Pressed += key => Avalonia.Threading.Dispatcher.UIThread.Post(() => OnMediaKey(key));
        }
        TitleBar.DuplicatesRequested += (_, _) => ShowDuplicates();
    }

    void Actions.IActionHost.PlayInMiniPlayer(IReadOnlyList<long> assetIds)
    {
        // From the audio player (its menu): the song goes on from where it is, as with Alt+"‹ Library".
        if (_mediaViewer?.Model is { Kind: MediaKind.Audio } media && media.Current?.AssetId is { } current
            && assetIds is [var only] && only == current)
        {
            HandToMiniPlayer(media);
            return;
        }
        _ = PlayInMiniPlayerAsync(assetIds);
    }

    private async Task PlayInMiniPlayerAsync(IReadOnlyList<long> assetIds)
    {
        if (ActiveModel is not { } model || _nowPlaying is null || assetIds.Count == 0)
        {
            return;
        }
        var first = assetIds[0];
        List<long> ids;
        if (assetIds.Count == 1)
        {
            var assets = await model.Store.AssetsAsync([first]);
            if (assets.Count == 0)
            {
                return;
            }
            ids = await PagingListAsync(model, assets[0]);
        }
        else
        {
            ids = [.. assetIds];
        }
        var rows = (await model.Store.AssetsAsync(ids)).ToDictionary(row => row.Id);
        var items = ids.Where(rows.ContainsKey).Where(id => rows[id].Kind == MediaKind.Audio).Select(id => PlaybackItem.From(rows[id])).ToList();
        _nowPlaying.Play(items, Math.Max(0, items.FindIndex(item => item.AssetId == first)));
    }

    /// <summary>
    /// The keyboard's media keys (macOS D-92, W-84) are taken only while a full player is open or the mini player is
    /// up; otherwise they stay with other programs.
    /// </summary>
    private void UpdateMediaKeys()
    {
        if (_mediaKeys is { } keys)
        {
            keys.IsActive = _mediaViewer is not null || _nowPlaying?.Playback is not null;
        }
    }

    /// <summary>Play/pause, next, previous: the open video or audio player, else the mini player.</summary>
    private void OnMediaKey(Platform.Windows.MediaKey key)
    {
        if (_mediaViewer is { } viewer)
        {
            viewer.PerformMediaKey(key switch
            {
                Platform.Windows.MediaKey.Next => MediaCommand.Next,
                Platform.Windows.MediaKey.Previous => MediaCommand.Previous,
                _ => MediaCommand.TogglePlay,
            });
        }
        else if (_nowPlaying?.Playback is { } playback)
        {
            switch (key)
            {
                case Platform.Windows.MediaKey.Next:
                    playback.Next();
                    break;
                case Platform.Windows.MediaKey.Previous:
                    playback.Previous();
                    break;
                default:
                    playback.TogglePlay();
                    break;
            }
        }
    }

    /// <summary>The audio player's song continues in the mini player: same queue, same song, same second.</summary>
    private void HandToMiniPlayer(MediaViewerModel media)
    {
        if (_nowPlaying is null)
        {
            ShowLibrary();
            return;
        }
        var playback = media.Playback;
        var items = playback.Queue.Items;
        var index = playback.Queue.Index;
        var time = playback.CurrentTime;
        var playing = playback.IsPlaying;
        // Close the full player first: its player stops, so the mini player does not pause for it.
        ShowLibrary();
        _nowPlaying.Play(items, index, time, autoplay: playing);
    }

    private Duplicates.DuplicatesWindow? _duplicates;

    /// <summary>Edit ▸ Find Duplicates… (W-78): one window, brought forward when open.</summary>
    public Duplicates.DuplicatesWindow? ShowDuplicates()
    {
        if (_model is not { } model || _fileOps is null)
        {
            return null;
        }
        if (_duplicates is { } open)
        {
            open.Activate();
            return open;
        }
        var window = new Duplicates.DuplicatesWindow(model.Services, _fileOps);
        window.RevealRequested += async (_, id) => await ShowInLibraryAsync(id);
        window.Closed += (_, _) => _duplicates = null;
        _duplicates = window;
        window.Show(this);
        return window;
    }

    /// <summary>The folder name in the bar: the library shows the song's folder, the song selected and scrolled into view.</summary>
    private async Task ShowInLibraryAsync(long assetId)
    {
        if (ActiveModel is not { } model)
        {
            return;
        }
        var assets = await model.Store.AssetsAsync([assetId]);
        if (assets.Count == 0)
        {
            return;
        }
        if (IsViewerOpen)
        {
            ShowLibrary();
        }
        var asset = assets[0];
        if (model.State.FolderId != asset.FolderId || model.State.AlbumId is not null)
        {
            // Its files arrive later; a selection made now would be dropped with the last folder's.
            var loaded = model.NextRefsAsync();
            model.SelectFolder(asset.FolderId);
            await loaded;
        }
        if (!model.Order.Contains(assetId) && !model.State.KindSet.Contains(asset.Kind))
        {
            // The kind filter hides it.
            var loaded = model.NextRefsAsync();
            model.ToggleKind(asset.Kind);
            await loaded;
        }
        Library.Reveal(assetId);
    }
}
