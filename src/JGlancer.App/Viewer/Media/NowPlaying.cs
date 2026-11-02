using Avalonia.Media.Imaging;
using JGlancer.App.Services;
using JGlancer.Core.Model;
using JGlancer.Core.Playback;
using JGlancer.Media.Thumbnails;

namespace JGlancer.App.Viewer.Media;

/// <summary>
/// The mini player's state (W-68): songs chosen with "Play in Mini Player" play in a bar at the bottom
/// of the window while the library, the viewers and the previews stay usable. Its own player and queue (the files of
/// the folder shown, or the songs selected), the remembered volume, speed and "play next"; another player starting
/// pauses it and the other way round (<see cref="MediaPlayback"/>). Closing the bar stops and drops the player.
/// </summary>
internal sealed class NowPlaying(AppServices services) : IDisposable
{
    private CancellationTokenSource? _coverLoad;
    private PlaybackItem? _shownItem;

    /// <summary>The player while the bar is up; null when closed.</summary>
    public MediaPlayback? Playback { get; private set; }

    /// <summary>The current song's cover once loaded (null: none, or not yet).</summary>
    public Bitmap? Cover { get; private set; }

    public PlaybackItem? Current => Playback?.Queue.Current;

    /// <summary>The folder the current song is in.</summary>
    public string? FolderPath => Current is { } item ? Path.GetDirectoryName(item.Path) : null;

    /// <summary>Another song, a cover, opened or closed (not the time: <see cref="MediaPlayback.TimeChanged"/>).</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Plays <paramref name="items"/> from <paramref name="index"/> (at <paramref name="start"/> seconds), in place of
    /// what played.
    /// </summary>
    public void Play(IReadOnlyList<PlaybackItem> items, int index, double start = 0, bool autoplay = true)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return;
        }
        if (Playback is null)
        {
            Playback = new MediaPlayback(MediaKind.Audio, services.PlaybackPreferences) { ResumesAfterOthers = true };
            Playback.Changed += OnPlaybackChanged;
        }
        Playback.Load(new PlaybackQueue(items, Math.Clamp(index, 0, items.Count - 1)), autoplay, start);
        OnPlaybackChanged(this, EventArgs.Empty);
    }

    /// <summary>Stops and closes the bar.</summary>
    public void Close()
    {
        if (Playback is not { } playback)
        {
            return;
        }
        Playback = null;
        playback.Changed -= OnPlaybackChanged;
        playback.Dispose();
        _shownItem = null;
        SetCover(null);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlaybackChanged(object? sender, EventArgs e)
    {
        if (Current is { } item && !ReferenceEquals(item, _shownItem))
        {
            _shownItem = item;
            SetCover(null);
            _ = LoadCoverAsync(item);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadCoverAsync(PlaybackItem item)
    {
        _coverLoad?.Cancel();
        var cancel = new CancellationTokenSource();
        _coverLoad = cancel;
        try
        {
            var image = await services.Thumbnails.GetAsync(MediaViewerModel.Thumbnail(item, ThumbnailBucket.Small), ThumbnailPriority.Visible, cancel.Token);
            var cover = image is null || cancel.IsCancellationRequested ? null : await Task.Run(() => Library.ThumbnailCache.ToBitmap(image));
            if (cancel.IsCancellationRequested || !ReferenceEquals(item, _shownItem))
            {
                cover?.Dispose();
                return;
            }
            SetCover(cover);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SetCover(Bitmap? cover)
    {
        var old = Cover;
        Cover = cover;
        // After the bar has let go of it.
        if (old is not null)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(old.Dispose, Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    public void Dispose()
    {
        _coverLoad?.Cancel();
        Close();
    }
}
