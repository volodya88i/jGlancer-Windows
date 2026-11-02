using JGlancer.App.Library;
using JGlancer.App.Services;
using JGlancer.App.Support;
using JGlancer.Core.Model;
using JGlancer.Core.Playback;
using JGlancer.Media.Thumbnails;

namespace JGlancer.App.Viewer.Media;

/// <summary>A key or button command of the video and audio viewer (macOS MediaCommand, FEATURES §4.2–4.3, §15).</summary>
internal enum MediaCommand
{
    TogglePlay,
    SeekBack,
    SeekForward,
    Previous,
    Next,
    StepBack,
    StepForward,
    Slower,
    Faster,
    ResetSpeed,
    VolumeUp,
    VolumeDown,
    ToggleFullScreen,
    ToggleSegment,
    ToggleMute,
    SaveFrame,
    CopyFrame,
}

/// <summary>
/// The video or audio viewer's state (macOS MediaViewerModel): one player for the files of one kind from one folder
/// (D-09), in the library's sort, with the remembered speed, volume and "play next"; also the filmstrip's source.
/// </summary>
internal sealed class MediaViewerModel : IFilmstripSource, IDisposable
{
    /// <summary>Seconds moved by ← and →.</summary>
    public const double SeekStep = 5;

    /// <summary>Volume change of ↑ and ↓.</summary>
    public const double VolumeStep = 0.1;

    private readonly AppServices _services;
    private int _shownIndex = -1;
    private PlaybackItem? _shownItem;

    public MediaViewerModel(AppServices services, MediaKind kind, IReadOnlyList<PlaybackItem> items, int index, JsonFile preferences)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        Kind = kind;
        Playback = new MediaPlayback(kind, preferences);
        Playback.Changed += (_, _) =>
        {
            // Another file: another position, or the same one after the file there went to the Recycle Bin.
            if (Playback.Queue.Index != _shownIndex || !ReferenceEquals(Playback.Queue.Current, _shownItem))
            {
                _shownIndex = Playback.Queue.Index;
                _shownItem = Playback.Queue.Current;
                // Another file: the segment belonged to the last one.
                EndSegmentMode();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
        _queue = new PlaybackQueue(items, index);
    }

    private PlaybackQueue? _queue;

    /// <summary>
    /// Starts playing. A video waits for its surface: mpv must have the render context before it opens a file, or it
    /// plays without a picture.
    /// </summary>
    public void Start()
    {
        if (_queue is { } queue)
        {
            _queue = null;
            Playback.Load(queue);
        }
    }

    public MediaKind Kind { get; }

    public MediaPlayback Playback { get; }

    public PlaybackItem? Current => Playback.Queue.Current;

    public bool IsFailed => Playback.Status == PlaybackStatus.Failed;

    /// <summary>Raised when another file became current (filmstrip, top bar).</summary>
    public event EventHandler? Changed;

    // Rows are complete from the start: queue items carry their names.
    public event EventHandler? RowsLoaded
    {
        add { }
        remove { }
    }

    public int Count => Playback.Queue.Count;

    public int Index => Playback.Queue.Index;

    public ThumbnailCache Bitmaps => _services.Bitmaps;

    public ThumbnailService Thumbnails => _services.Thumbnails;

    public FilmstripItem? Item(int index, ThumbnailBucket bucket)
    {
        var items = Playback.Queue.Items;
        return index >= 0 && index < items.Count ? new FilmstripItem(items[index].Name, items[index].Kind, Thumbnail(items[index], bucket)) : null;
    }

    public void PrepareRows(int index)
    {
    }

    public void MoveTo(int index) => Playback.Select(index);

    /// <summary>A thumbnail request for a queue item (filmstrip, cover art).</summary>
    public static ThumbnailRequest Thumbnail(PlaybackItem item, ThumbnailBucket bucket)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new ThumbnailRequest(item.AssetId ?? 0, item.Path, item.Kind, item.TypeIdentifier,
            item.ModifiedAt ?? DateTimeOffset.UnixEpoch, item.Duration, bucket);
    }

    // MARK: Segment (D-73, D-74)

    /// <summary>The selected part of the current file while selecting a segment; null when the mode is off.</summary>
    public SegmentRange? Segment { get; private set; }

    public bool IsSelectingSegment => Segment is not null;

    /// <summary>Whether the current file can be cut (video: MP4, MOV, M4V).</summary>
    public bool CanExportSegment => Current is { } item && Segments.CanExport(item.Path, Kind);

    /// <summary>Raised when the segment, the mode or its export settings changed.</summary>
    public event EventHandler? SegmentChanged;

    public void ToggleSegmentMode()
    {
        if (IsSelectingSegment)
        {
            EndSegmentMode();
        }
        else if (Playback.Duration is { } duration && duration > 0)
        {
            SetSegment(Segments.Default(duration, Playback.CurrentTime));
        }
    }

    /// <summary>Leaves the mode (also when another file becomes current or the viewer closes).</summary>
    public void EndSegmentMode()
    {
        if (Segment is null)
        {
            return;
        }
        Segment = null;
        Playback.StopTime = null;
        SegmentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A bracket was dragged (D-74): the playhead follows the start bracket; playback stops at the end one.</summary>
    /// <param name="followPlayhead">False with Alt held: the bracket moves, the playhead stays.</param>
    public void MoveSegment(SegmentRange range, bool isStart, bool followPlayhead = true)
    {
        SetSegment(range);
        if (!followPlayhead)
        {
            return;
        }
        if (isStart)
        {
            // Exact while dragging too: keyframe seeks made the playhead jump about (as with the end bracket).
            Playback.Seek(range.Start, exact: true);
        }
        else if (Kind == MediaKind.Video)
        {
            // The picture shows the frame under the end bracket while it is dragged: exact seeks (mpv drops the ones
            // it has not reached), so the playhead stays with the bracket instead of jumping between keyframes.
            Playback.Seek(range.End, exact: true);
        }
    }

    private void SetSegment(SegmentRange range)
    {
        Segment = range;
        Playback.StopTime = range.End;
        SegmentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleSegmentFades()
    {
        Playback.SetSegmentFades(!Playback.Preferences.SegmentFades);
        SegmentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>What the export button saves: the segment, with the speed, Keep Pitch and fades for audio.</summary>
    public SegmentExportRequest? ExportRequest => Segment is { } segment && Current is { } item && CanExportSegment
        ? new SegmentExportRequest(item.Path, Kind, segment,
            Kind == MediaKind.Audio ? Playback.Rate : PlaybackSpeed.Normal,
            Playback.PreservesPitch,
            Kind == MediaKind.Audio && Playback.Preferences.SegmentFades,
            Playback.Preferences.SegmentKeepsAudioFormat)
        : null;

    /// <summary>Play / pause; stopped at the segment's end, Play starts again from its start.</summary>
    private void TogglePlayInSegment()
    {
        if (Segment is { } segment && !Playback.IsPlaying && Math.Abs(Playback.CurrentTime - segment.End) < Segments.EndTolerance)
        {
            Playback.Seek(segment.Start, exact: true);
        }
        Playback.TogglePlay();
    }

    /// <summary>Runs a command; full screen belongs to the window and is handled by the view.</summary>
    public void Perform(MediaCommand command)
    {
        switch (command)
        {
            case MediaCommand.TogglePlay:
                TogglePlayInSegment();
                break;
            case MediaCommand.SeekBack:
                Playback.Skip(-SeekStep);
                break;
            case MediaCommand.SeekForward:
                Playback.Skip(SeekStep);
                break;
            case MediaCommand.Previous:
                Playback.Previous();
                break;
            case MediaCommand.Next:
                Playback.Next();
                break;
            case MediaCommand.StepBack:
                Playback.Step(-1);
                break;
            case MediaCommand.StepForward:
                Playback.Step(1);
                break;
            case MediaCommand.Slower:
                Playback.SetRate(PlaybackSpeed.Stepped(Playback.Rate, -1));
                break;
            case MediaCommand.Faster:
                Playback.SetRate(PlaybackSpeed.Stepped(Playback.Rate, 1));
                break;
            case MediaCommand.ResetSpeed:
                Playback.SetRate(PlaybackSpeed.Normal);
                break;
            case MediaCommand.VolumeUp:
                Playback.SetVolume(Playback.Volume + VolumeStep);
                break;
            case MediaCommand.VolumeDown:
                Playback.SetVolume(Playback.Volume - VolumeStep);
                break;
            case MediaCommand.ToggleSegment:
                ToggleSegmentMode();
                break;
            case MediaCommand.ToggleMute:
                Playback.ToggleMute();
                break;
        }
    }

    public void Dispose() => Playback.Dispose();
}
