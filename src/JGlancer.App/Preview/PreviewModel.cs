using Avalonia.Media.Imaging;
using Avalonia.Threading;
using JGlancer.App.Library;
using JGlancer.App.Services;
using JGlancer.App.Viewer.Media;
using JGlancer.Core.Model;
using JGlancer.Core.Playback;
using JGlancer.Design;
using JGlancer.Media.Thumbnails;
using JGlancer.Mpv;

namespace JGlancer.App.Preview;

/// <summary>
/// What the preview pane and Quick Preview show for the library's selection (macOS PreviewModel, FEATURES §2–3): the
/// previewed file (a photo decoded for the preview, or a video or song loaded into the previews' own player) and the
/// selection count. The pane never starts playback; Quick Preview autoplays. While arrows are held nothing heavy
/// starts: the decode, the player and the waveform wait until the selection rests (<see cref="SettleDelay"/>). While a
/// viewer is open the previews stand aside. UI thread only.
/// </summary>
internal sealed partial class PreviewModel : IDisposable
{
    /// <summary>Longest side, in pixels, of photo decodes for the previews (Quick Preview can be large; macOS 1600).</summary>
    public const int PhotoPixels = 1600;

    /// <summary>How long the selection rests before the full preview loads.</summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(120);

    private readonly AppServices _services;
    private SelectionModel _selection;
    private CancellationTokenSource? _load;
    private long? _previewedId;
    private bool _isSuspended;
    private bool _isDisposed;

    public PreviewModel(AppServices services, SelectionModel selection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(selection);
        _services = services;
        _selection = selection;
        selection.Changed += OnSelectionChanged;
        // After a burst of database writes (details read, renames), once: the facts follow.
        _rowRefresh = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, async (_, _) =>
        {
            _rowRefresh!.Stop();
            await RefreshRowAsync();
        });
        _rowRefresh.Stop();
        services.Store.Changed += OnStoreChanged;
    }

    private readonly DispatcherTimer _rowRefresh;

    private void OnStoreChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (!_isDisposed && Asset is not null && !_rowRefresh.IsEnabled)
        {
            _rowRefresh.Start();
        }
    });

    /// <summary>The previewed file, once its row is loaded.</summary>
    public Asset? Asset { get; private set; }

    /// <summary>Selected files (the pane summarizes several).</summary>
    public int SelectionCount => _selection.Count;

    /// <summary>A photo (its thumbnail, then a decode at preview size), or a video's poster (its thumbnail).</summary>
    public Bitmap? Picture { get; private set; }

    /// <summary>A song's cover art, if it has one.</summary>
    public Bitmap? Cover { get; private set; }

    /// <summary>The cover is known (null <see cref="Cover"/> then means the song has none).</summary>
    public bool IsCoverKnown { get; private set; }

    public JGWaveformPeaks? Waveform { get; private set; }

    /// <summary>The player of the previewed video or song (one per kind, made on first use).</summary>
    public MediaPlayback? Playback { get; private set; }

    /// <summary>The video picture of <see cref="Playback"/>; one window shows it at a time (W-41).</summary>
    public MpvVideoView? Surface { get; private set; }

    /// <summary>The player holds the previewed file.</summary>
    public bool OwnsPlayback { get; private set; }

    public bool IsQuickPreviewOpen { get; private set; }

    /// <summary>Quick Preview starts video and audio by itself (Settings → Playback).</summary>
    public static bool AutoplayInPreview => Settings.SettingsStore.Current.Settings.AutoplayInPreview;

    public bool IsFailed => OwnsPlayback && Playback?.Status == PlaybackStatus.Failed;

    /// <summary>Everything for the previewed file has loaded (developer snapshots wait for it).</summary>
    public bool IsComplete { get; private set; } = true;

    /// <summary>Raised when the file, its picture, cover or waveform, the selection count or the player changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when Quick Preview opened or closed: the video surface moves.</summary>
    public event EventHandler? SurfaceMoved;

    // MARK: Selection

    private void OnSelectionChanged(object? sender, EventArgs e) => SelectionChanged();

    /// <summary>The split view's other pane became the active one: its selection is previewed from now on.</summary>
    public void Follow(SelectionModel selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (ReferenceEquals(selection, _selection))
        {
            return;
        }
        _selection.Changed -= OnSelectionChanged;
        _selection = selection;
        _selection.Changed += OnSelectionChanged;
        SelectionChanged();
    }

    private void SelectionChanged()
    {
        var previewed = _selection.Previewed;
        if (previewed == _previewedId)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }
        _previewedId = previewed;
        if (!_isSuspended)
        {
            Load(previewed);
        }
        else
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>A viewer opened: the previews let go of their file and stop playing.</summary>
    public void Suspend()
    {
        _isSuspended = true;
        _load?.Cancel();
        StopPlayback();
    }

    /// <summary>The viewer closed: show the previewed file again, paused.</summary>
    public void Resume()
    {
        _isSuspended = false;
        _previewedId = _selection.Previewed;
        Load(_previewedId);
    }

    /// <summary>The previewed file is about to move or go to the Recycle Bin: the player lets go of it.</summary>
    public void Release(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (OwnsPlayback && Asset is { } asset && paths.Any(path => Core.Store.LibraryPaths.IsSameOrInside(asset.Path, path)))
        {
            StopPlayback();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// The library's index changed (details read, a file renamed): the facts follow; the picture and the player stay.
    /// </summary>
    public async Task RefreshRowAsync()
    {
        if (Asset is not { } shown || _isSuspended)
        {
            return;
        }
        var rows = await _services.AssetsAsync([shown.Id]);
        if (rows.Count > 0 && ReferenceEquals(Asset, shown) && rows[0] != shown && rows[0].Kind == shown.Kind)
        {
            Asset = rows[0];
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    // MARK: Quick Preview

    /// <summary>The pane is on screen. With neither the pane nor Quick Preview, only the file's row is loaded.</summary>
    public bool IsPaneShown { get; private set; } = true;

    private bool IsShown => IsPaneShown || IsQuickPreviewOpen;

    public void SetPaneShown(bool shown)
    {
        if (IsPaneShown == shown)
        {
            return;
        }
        var wasShown = IsShown;
        IsPaneShown = shown;
        if (IsShown != wasShown && !_isSuspended)
        {
            Load(_previewedId);
        }
    }

    public void SetQuickPreviewOpen(bool open)
    {
        if (IsQuickPreviewOpen == open)
        {
            return;
        }
        var wasShown = IsShown;
        IsQuickPreviewOpen = open;
        SurfaceMoved?.Invoke(this, EventArgs.Empty);
        if (IsShown != wasShown && !_isSuspended)
        {
            // Nothing was loaded for a hidden pane (or nothing is shown any more).
            Load(_previewedId);
            return;
        }
        if (OwnsPlayback && Playback is { } playback)
        {
            if (open && AutoplayInPreview)
            {
                playback.Play();
            }
            else if (!open)
            {
                playback.Pause();
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePlay()
    {
        if (OwnsPlayback)
        {
            Playback?.TogglePlay();
        }
    }

    // MARK: Loading

    private void Load(long? id)
    {
        _load?.Cancel();
        if (id is not { } assetId)
        {
            Clear();
            return;
        }
        var cancel = new CancellationTokenSource();
        _load = cancel;
        IsComplete = false;
        _ = ShowAsync(assetId, cancel.Token);
    }

    private void Clear()
    {
        Asset = null;
        StopFrames();
        Frames = null;
        _frameRows = [];
        SetPicture(null);
        SetCover(null, known: false);
        Waveform = null;
        StopPlayback();
        IsComplete = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task ShowAsync(long id, CancellationToken cancellation)
    {
        try
        {
            var rows = await _services.AssetsAsync([id]);
            var asset = rows.Count > 0 ? rows[0] : null;
            if (cancellation.IsCancellationRequested)
            {
                return;
            }
            if (asset is null)
            {
                Clear();
                return;
            }
            Asset = asset;
            SetPicture(null);
            SetCover(null, known: false);
            Waveform = null;
            StopPlayback();
            // The views drop the old picture now: it is disposed right after, and nothing may draw it while the
            // sequence's frames are read (a crash when the selection changed fast, e.g. files moving).
            Changed?.Invoke(this, EventArgs.Empty);
            await LoadFramesAsync(asset, cancellation);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }
            Changed?.Invoke(this, EventArgs.Empty);
            if (!IsShown)
            {
                IsComplete = true;
                return;
            }

            // Right away: the thumbnail as the picture, the video's poster or the song's cover.
            var thumbnail = await Thumbnail(asset, cancellation);
            if (cancellation.IsCancellationRequested)
            {
                thumbnail?.Dispose();
                return;
            }
            if (asset.Kind == MediaKind.Audio)
            {
                SetCover(thumbnail, known: true);
            }
            else
            {
                SetPicture(thumbnail);
            }
            Changed?.Invoke(this, EventArgs.Empty);

            // Once the selection rests: the photo at preview size, the player, the waveform.
            await Task.Delay(SettleDelay, cancellation);
            switch (asset.Kind)
            {
                case MediaKind.Photo:
                    await ShowPhotoAsync(asset, cancellation);
                    break;
                default:
                    StartPlayer(asset);
                    if (asset.Kind == MediaKind.Audio)
                    {
                        await ShowWaveformAsync(asset, cancellation);
                    }
                    break;
            }
            IsComplete = !cancellation.IsCancellationRequested;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<Bitmap?> Thumbnail(Asset asset, CancellationToken cancellation)
    {
        var request = MediaViewerModel.Thumbnail(PlaybackItem.From(asset), ThumbnailBucket.Large);
        var image = await _services.Thumbnails.GetAsync(request, ThumbnailPriority.Visible, cancellation);
        return image is null ? null : await Task.Run(() => ThumbnailCache.ToBitmap(image), cancellation);
    }

    private async Task ShowPhotoAsync(Asset asset, CancellationToken cancellation)
    {
        var bitmap = await Task.Run(() =>
        {
            var decoded = _services.Photos.Decode(asset.Path, PhotoPixels, PhotoPixels, cancellation);
            return decoded is null ? null : ThumbnailCache.ToBitmap(decoded.Image);
        }, cancellation);
        if (cancellation.IsCancellationRequested)
        {
            bitmap?.Dispose();
            return;
        }
        // A sequence scrubbed past its first frame meanwhile keeps the frame shown.
        if (bitmap is not null && FrameIndex == 0)
        {
            SetPicture(bitmap);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            bitmap?.Dispose();
        }
    }

    private async Task ShowWaveformAsync(Asset asset, CancellationToken cancellation)
    {
        var peaks = await MpvAudioDecoder.PeaksAsync(asset.Path, cancellation);
        if (!cancellation.IsCancellationRequested && peaks is { } found)
        {
            Waveform = new JGWaveformPeaks(found.Lows, found.Highs);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetPicture(Bitmap? picture)
    {
        var old = Picture;
        Picture = picture;
        if (!ReferenceEquals(old, picture))
        {
            // The views switch first (Changed), then the old one goes; the renderer keeps what it still draws.
            Dispatcher.UIThread.Post(() => old?.Dispose(), DispatcherPriority.Background);
        }
    }

    private void SetCover(Bitmap? cover, bool known)
    {
        var old = Cover;
        Cover = cover;
        IsCoverKnown = known;
        if (!ReferenceEquals(old, cover))
        {
            Dispatcher.UIThread.Post(() => old?.Dispose(), DispatcherPriority.Background);
        }
    }

    // MARK: Player

    private void StartPlayer(Asset asset)
    {
        if (!MpvPlayer.IsAvailable)
        {
            return;
        }
        var playback = PlaybackFor(asset.Kind);
        var queue = new PlaybackQueue([PlaybackItem.From(asset)], 0);
        OwnsPlayback = true;
        if (asset.Kind == MediaKind.Audio || Surface?.Released.IsCompleted == false)
        {
            // Audio needs no surface; a video surface already in a window takes the file right away.
            playback.Load(queue, autoplay: false);
            Begin(playback);
        }
        else
        {
            // The surface opens it once it has a render context (RenderReady).
            playback.Prepare(queue);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Quick Preview plays as soon as the file is there.</summary>
    private void Begin(MediaPlayback playback)
    {
        if (IsQuickPreviewOpen && AutoplayInPreview && OwnsPlayback)
        {
            playback.Play();
        }
    }

    /// <summary>The player of <paramref name="kind"/>; a player of the other kind is retired first.</summary>
    private MediaPlayback PlaybackFor(MediaKind kind)
    {
        if (Playback is { } current && current.Kind == kind)
        {
            return current;
        }
        RetirePlayback();
        var playback = new MediaPlayback(kind, _services.PlaybackPreferences);
        playback.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        Playback = playback;
        if (kind == MediaKind.Video)
        {
            var surface = new MpvVideoView(playback.Player);
            // Every new render context (first show, or a move between the pane and Quick Preview) takes the file
            // where it is: mpv dropped the video track with the old context.
            surface.RenderReady += (_, _) =>
            {
                if (Playback == playback && OwnsPlayback)
                {
                    playback.Reattach();
                    Begin(playback);
                }
            };
            Surface = surface;
        }
        SurfaceMoved?.Invoke(this, EventArgs.Empty);
        return playback;
    }

    private void StopPlayback()
    {
        if (OwnsPlayback)
        {
            Playback?.Stop();
            OwnsPlayback = false;
        }
    }

    /// <summary>The player goes once its surface left every window and freed its render context (mpv's rule).</summary>
    private void RetirePlayback()
    {
        if (Playback is not { } playback)
        {
            return;
        }
        playback.Stop();
        OwnsPlayback = false;
        var surface = Surface;
        Playback = null;
        Surface = null;
        SurfaceMoved?.Invoke(this, EventArgs.Empty);
        var released = surface?.Released ?? Task.CompletedTask;
        _ = released.ContinueWith(_ => Dispatcher.UIThread.Post(playback.Dispose), TaskScheduler.Default);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;
        _services.Store.Changed -= OnStoreChanged;
        _rowRefresh.Stop();
        _load?.Cancel();
        StopFrames();
        RetirePlayback();
        SetPicture(null);
        SetCover(null, known: false);
    }
}
