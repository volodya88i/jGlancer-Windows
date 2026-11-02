using Avalonia.Media.Imaging;
using Avalonia.Threading;
using JGlancer.App.Library;
using JGlancer.App.Services;
using JGlancer.App.Viewer.Media;
using JGlancer.Core.Model;
using JGlancer.Core.Playback;
using JGlancer.Design;
using JGlancer.Media.Thumbnails;

namespace JGlancer.App.Preview;

/// <summary>
/// A folded image sequence in the previews (W-86): its frames scrub like a video's timeline and play at
/// <see cref="JGMetrics.SequencePreviewRate"/> frames a second. Each frame shows its large thumbnail; only the newest
/// request loads (a drag or playback never queues frames behind), and frames not ready yet are skipped while playing.
/// </summary>
internal sealed partial class PreviewModel
{
    private IReadOnlyList<Asset> _frameRows = [];
    private DispatcherTimer? _framePlayer;
    private int? _wantedFrame;
    private bool _loadingFrame;

    /// <summary>The frames of a folded sequence by its first frame's id (the active pane's grid); null otherwise.</summary>
    public Func<long, IReadOnlyList<long>?>? SequenceFrames { get; set; }

    /// <summary>The previewed sequence's frames (its first is <see cref="Asset"/>), or null for any other file.</summary>
    public IReadOnlyList<long>? Frames { get; private set; }

    /// <summary>The frame shown (0 = the first).</summary>
    public int FrameIndex { get; private set; }

    public bool IsPlayingFrames => _framePlayer?.IsEnabled == true;

    /// <summary>The frame shown changed (lighter than <see cref="Changed"/>: the picture, the playhead and the count).</summary>
    public event EventHandler? FrameChanged;

    /// <summary>A new file is previewed: its frames when it heads a folded sequence.</summary>
    private async Task LoadFramesAsync(Asset asset, CancellationToken cancellation)
    {
        StopFrames();
        Frames = null;
        FrameIndex = 0;
        _frameRows = [];
        if (asset.Kind != MediaKind.Photo || SequenceFrames?.Invoke(asset.Id) is not { Count: > 1 } frames)
        {
            return;
        }
        var rows = await _services.AssetsAsync(frames);
        if (cancellation.IsCancellationRequested || rows.Count < 2)
        {
            return;
        }
        _frameRows = rows;
        Frames = [.. rows.Select(row => row.Id)];
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A stack folded or unfolded: the previewed head gets or loses its frame timeline.</summary>
    public async Task SequencesChangedAsync()
    {
        if (Asset is not { Kind: MediaKind.Photo } asset)
        {
            return;
        }
        var folded = SequenceFrames?.Invoke(asset.Id) is { Count: > 1 };
        if (folded == (Frames is not null))
        {
            return;
        }
        await LoadFramesAsync(asset, CancellationToken.None);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows frame <paramref name="index"/> (a scrub or a step); playback goes on from there.</summary>
    public void ShowFrame(int index)
    {
        if (_frameRows.Count == 0)
        {
            return;
        }
        index = Math.Clamp(index, 0, _frameRows.Count - 1);
        if (index == FrameIndex && _wantedFrame is null)
        {
            return;
        }
        _wantedFrame = index;
        if (!_loadingFrame)
        {
            _ = LoadFrameAsync();
        }
    }

    /// <summary>Plays the frames from the one shown (from the start after the last), or pauses.</summary>
    public void ToggleFrames()
    {
        if (_frameRows.Count == 0)
        {
            return;
        }
        if (IsPlayingFrames)
        {
            StopFrames();
        }
        else
        {
            if (FrameIndex >= _frameRows.Count - 1)
            {
                ShowFrame(0);
            }
            _framePlayer ??= new DispatcherTimer(TimeSpan.FromSeconds(1 / JGMetrics.SequencePreviewRate), DispatcherPriority.Render, (_, _) => NextFrame());
            _framePlayer.Start();
        }
        FrameChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NextFrame()
    {
        // Still loading the last one: this tick is skipped rather than queued.
        if (_loadingFrame)
        {
            return;
        }
        if (FrameIndex >= _frameRows.Count - 1)
        {
            StopFrames();
            FrameChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        ShowFrame(FrameIndex + 1);
    }

    private void StopFrames() => _framePlayer?.Stop();

    private async Task LoadFrameAsync()
    {
        _loadingFrame = true;
        try
        {
            while (_wantedFrame is { } index && _frameRows.Count > index)
            {
                _wantedFrame = null;
                var rows = _frameRows;
                var request = MediaViewerModel.Thumbnail(PlaybackItem.From(rows[index]), ThumbnailBucket.Large);
                var image = await _services.Thumbnails.GetAsync(request, ThumbnailPriority.Visible, CancellationToken.None);
                Bitmap? bitmap = image is null ? null : await Task.Run(() => ThumbnailCache.ToBitmap(image));
                // Another file previewed meanwhile: this frame is not wanted any more.
                if (!ReferenceEquals(rows, _frameRows))
                {
                    bitmap?.Dispose();
                    return;
                }
                FrameIndex = index;
                if (bitmap is not null)
                {
                    SetPicture(bitmap);
                }
                FrameChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _loadingFrame = false;
        }
    }
}
