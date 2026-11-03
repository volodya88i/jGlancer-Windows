using Avalonia.Threading;
using JGlancer.App.Support;
using JGlancer.Core.Model;
using JGlancer.Core.Playback;
using JGlancer.Mpv;

namespace JGlancer.App.Viewer.Media;

/// <summary>Where the current file is.</summary>
internal enum PlaybackStatus
{
    Idle,
    Loading,
    Ready,
    Failed,
}

/// <summary>
/// Plays the items of a <see cref="PlaybackQueue"/> with one mpv player (macOS PlaybackController): play, pause,
/// seek, frame steps, speed, volume, loop and "play next", with the remembered speed per kind, volume and Keep Pitch
/// (macOS PlaybackTuning). UI thread only; mpv's events are posted here. The time is pushed to
/// <see cref="TimeChanged"/> on every update (playheads); <see cref="Changed"/> fires for everything else. One plays
/// at a time: when a player starts, any other that plays pauses (the mini player and a viewer or Quick Preview, W-68);
/// one that <see cref="ResumesAfterOthers"/> plays on once the player that paused it pauses or closes.
/// </summary>
internal sealed class MediaPlayback : IDisposable
{
    private readonly JsonFile _preferencesFile;
    private bool _disposed;

    /// <summary>A player started playing (the others pause).</summary>
    private static event Action<MediaPlayback>? Started;

    /// <summary>A player paused or closed (one it paused may play on).</summary>
    private static event Action<MediaPlayback>? Stopped;

    /// <summary>The player that paused this one by starting, while this one waits to play on.</summary>
    private MediaPlayback? _pausedBy;

    public MediaPlayback(MediaKind kind, JsonFile preferencesFile)
    {
        ArgumentNullException.ThrowIfNull(preferencesFile);
        Kind = kind;
        _preferencesFile = preferencesFile;
        Preferences = SharedPreferences(preferencesFile);
        Player = new MpvPlayer(video: kind == MediaKind.Video);
        Player.TimeChanged += seconds => Post(() => SetTime(seconds));
        Player.DurationChanged += seconds => Post(() =>
        {
            Duration = seconds;
            Changed?.Invoke(this, EventArgs.Empty);
        });
        Player.PauseChanged += paused => Post(() =>
        {
            IsPlaying = !paused;
            Changed?.Invoke(this, EventArgs.Empty);
            if (Queue.Current is not null)
            {
                (paused ? Stopped : Started)?.Invoke(this);
            }
        });
        Started += OnStarted;
        Stopped += OnStopped;
        Player.FileLoaded += () =>
        {
            // Tags are read on mpv's thread, right when the file is open.
            var tags = (Title: Player.Tag("title"), Artist: Player.Tag("artist"), Album: Player.Tag("album"));
            Post(() => FileLoaded(tags));
        };
        void FileLoaded((string? Title, string? Artist, string? Album) tags)
        {
            (Title, Artist, Album) = tags;
            // A file that opened plays, even when the error of a load it replaced arrived late (W-41).
            if (Status is PlaybackStatus.Loading or PlaybackStatus.Failed)
            {
                Status = PlaybackStatus.Ready;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
        Player.Ended += reason => Post(() =>
        {
            if (reason == MpvEndReason.Error)
            {
                Status = PlaybackStatus.Failed;
                IsPlaying = false;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        });
        Player.EndReached += () => Post(ItemDidEnd);
        Player.VideoSizeChanged += (width, height) => Post(() =>
        {
            VideoSize = (width, height);
            Changed?.Invoke(this, EventArgs.Empty);
        });
        // mpv starts unpaused: its first "pause" report would read as playing before any file is open (the previews
        // open files paused).
        Player.SetPaused(true);
        Rate = Preferences.Rate(kind);
        AutoAdvance = Preferences.PlaysNext(kind);
        Player.SetSpeed(Rate);
        Player.SetVolume(Preferences.Volume);
        Player.SetPitchCorrection(Preferences.PreservesPitch);
        ApplyStretchFilter();
    }

    private string _stretchFilter = "";

    /// <summary>The filter for the current speed and Keep Pitch; set only when it changes, as setting it restarts the audio chain.</summary>
    private void ApplyStretchFilter()
    {
        var filter = PlaybackSpeed.StretchFilter(Rate, PreservesPitch);
        if (filter != _stretchFilter)
        {
            _stretchFilter = filter;
            Player.SetAudioFilters(filter);
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<JsonFile, PlaybackPreferences> Shared = [];

    /// <summary>
    /// One preferences object per file for every player and Settings → Playback, so none of them writes back a value
    /// another one changed.
    /// </summary>
    public static PlaybackPreferences SharedPreferences(JsonFile file) =>
        Shared.GetValue(file, f => PlaybackPreferences.FromJson(f.Read()));

    /// <summary>Saves the shared preferences (Settings → Playback).</summary>
    public static void SavePreferences(JsonFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        file.Write(SharedPreferences(file).ToJson());
    }

    public MediaKind Kind { get; }

    public MpvPlayer Player { get; }

    public PlaybackPreferences Preferences { get; }

    public PlaybackQueue Queue { get; private set; } = PlaybackQueue.Empty;

    public PlaybackStatus Status { get; private set; }

    public bool IsPlaying { get; private set; }

    /// <summary>Seconds into the current item.</summary>
    public double CurrentTime { get; private set; }

    /// <summary>Seconds; from the library until the file's own duration is known.</summary>
    public double? Duration { get; private set; }

    public double Rate { get; private set; }


    /// <summary>The speed the time moves at now: the rate while playing a loaded file, else 0 (smooth playheads).</summary>
    public double MovingRate => IsPlaying && Status == PlaybackStatus.Ready ? Rate : 0;

    public double Volume => Preferences.Volume;

    public bool IsMuted { get; private set; }

    public bool PreservesPitch => !IsTargeted && Preferences.PreservesPitch;

    /// <summary>Repeats the current item.</summary>
    public bool Loops { get; private set; }

    /// <summary>Continues with the next item at the end.</summary>
    public bool AutoAdvance { get; private set; }

    /// <summary>Playback pauses here (the end of a selected segment, D-74); null plays on. Cleared with another file.</summary>
    public double? StopTime { get; set; }

    /// <summary>Tags of the current file, once it is open (null when it has none).</summary>
    public string? Title { get; private set; }

    public string? Artist { get; private set; }

    public string? Album { get; private set; }

    /// <summary>The picture's display size (0 × 0 without video).</summary>
    public (int Width, int Height) VideoSize { get; private set; }

    /// <summary>Raised on every position update (playheads; many times a second while playing).</summary>
    public event EventHandler<double>? TimeChanged;

    /// <summary>Raised when anything but the position changed.</summary>
    public event EventHandler? Changed;

    private void Post(Action action) => Dispatcher.UIThread.Post(() =>
    {
        if (!_disposed)
        {
            action();
        }
    });

    // MARK: Queue

    /// <summary>Plays <paramref name="queue"/>'s current file, from <paramref name="start"/> seconds.</summary>
    public void Load(PlaybackQueue queue, bool autoplay = true, double start = 0)
    {
        ArgumentNullException.ThrowIfNull(queue);
        Queue = queue;
        LoadCurrent(autoplay, start: start);
    }

    /// <summary>
    /// Takes the queue without opening the file yet: a video waits for its surface's render context, then
    /// <see cref="Reattach"/> opens it (paused).
    /// </summary>
    public void Prepare(PlaybackQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        Queue = queue;
        LoadCurrent(autoplay: false, open: false);
    }

    /// <summary>
    /// Opens the current file again where it is, playing or paused as it was: a new video surface took over (mpv
    /// drops the video track when its render context goes, W-41).
    /// </summary>
    public void Reattach()
    {
        if (Queue.Current is not { } item || Status == PlaybackStatus.Idle)
        {
            return;
        }
        // Also after a failure: a file with only a video track ends with an error when its surface goes while it opens.
        Status = PlaybackStatus.Loading;
        Player.Load(item.Path, paused: !IsPlaying, start: CurrentTime);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Starts or continues playing (Quick Preview autoplay).</summary>
    public void Play()
    {
        if (!IsPlaying)
        {
            TogglePlay();
        }
    }

    public void Pause()
    {
        if (IsPlaying)
        {
            TogglePlay();
        }
    }

    public void Select(int index)
    {
        if (Queue.Select(index) is { } queue)
        {
            Queue = queue;
            LoadCurrent(autoplay: true);
        }
    }

    public void Next() => Select(Queue.Index + 1);

    /// <summary>The current file went to the Recycle Bin: the next one plays (the previous at the end). False when none is left.</summary>
    public bool RemoveCurrent()
    {
        Queue = Queue.RemovingCurrent();
        if (Queue.Current is null)
        {
            Stop();
            return false;
        }
        // As another file chosen in the viewer: it plays.
        LoadCurrent(autoplay: true);
        return true;
    }

    /// <summary>Back to the start of the item, or to the previous one in its first seconds.</summary>
    public void Previous()
    {
        switch (Queue.PreviousAction(CurrentTime))
        {
            case PreviousAction.Move move:
                Select(move.Index);
                break;
            default:
                Seek(0, exact: true);
                break;
        }
    }

    private void LoadCurrent(bool autoplay, bool open = true, double start = 0)
    {
        if (Queue.Current is not { } item)
        {
            Player.Stop();
            Status = PlaybackStatus.Idle;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }
        Status = PlaybackStatus.Loading;
        Duration = item.Duration;
        StopTime = null;
        LeaveTarget();
        (Title, Artist, Album) = (null, null, null);
        VideoSize = default;
        SetTime(start);
        if (open)
        {
            Player.Load(item.Path, paused: !autoplay, start: start);
        }
        IsPlaying = autoplay;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ItemDidEnd()
    {
        switch (Queue.EndAction(Loops, AutoAdvance))
        {
            case EndAction.Replay:
                Seek(0, exact: true);
                Player.SetPaused(false);
                break;
            case EndAction.Advance advance:
                Select(advance.Index);
                break;
            default:
                // keep-open leaves the last frame paused at the end.
                IsPlaying = false;
                Changed?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    // MARK: Transport

    /// <summary>Plays on when the player that paused it pauses or closes (the mini player, W-68).</summary>
    public bool ResumesAfterOthers { get; init; }

    private void OnStarted(MediaPlayback other)
    {
        if (!ReferenceEquals(other, this) && IsPlaying)
        {
            Pause();
            _pausedBy = ResumesAfterOthers ? other : null;
        }
    }

    private void OnStopped(MediaPlayback other)
    {
        if (_pausedBy is not null && ReferenceEquals(other, _pausedBy))
        {
            _pausedBy = null;
            if (!IsPlaying)
            {
                TogglePlay();
            }
        }
    }

    public void TogglePlay()
    {
        // The user's own play or pause: no waiting to play on.
        _pausedBy = null;
        if (Status == PlaybackStatus.Failed || Queue.Current is null)
        {
            return;
        }
        if (IsPlaying)
        {
            Player.SetPaused(true);
            IsPlaying = false;
        }
        else
        {
            // At the end, play starts over.
            if (Duration is { } duration && CurrentTime >= duration - 0.05)
            {
                Seek(0, exact: true);
            }
            Player.SetPaused(false);
            IsPlaying = true;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Jumps to <paramref name="seconds"/>: exact to the frame, or the nearest keyframe while scrubbing.</summary>
    public void Seek(double seconds, bool exact)
    {
        var target = Math.Clamp(seconds, 0, Duration ?? double.MaxValue);
        Player.Seek(target, exact);
        SetTime(target);
    }

    public void Skip(double seconds) => Seek(CurrentTime + seconds, exact: true);

    /// <summary>Pauses and moves by whole video frames.</summary>
    public void Step(int frames)
    {
        if (Kind != MediaKind.Video || Status == PlaybackStatus.Failed)
        {
            return;
        }
        Player.Step(frames);
        IsPlaying = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Plays the current song at <paramref name="rate"/> without Keep Pitch, to reach a pitch or a tempo (W-108). It is for
    /// this song only: the saved speed and Keep Pitch come back with the next file.
    /// </summary>
    public void SetTargetRate(double rate)
    {
        IsTargeted = true;
        Rate = rate;
        Player.SetSpeed(Rate);
        Player.SetPitchCorrection(false);
        ApplyStretchFilter();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Back to the saved speed and Keep Pitch.</summary>
    public void ClearTarget()
    {
        LeaveTarget();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void LeaveTarget()
    {
        if (IsTargeted)
        {
            IsTargeted = false;
            Rate = Preferences.Rate(Kind);
            Player.SetSpeed(Rate);
            Player.SetPitchCorrection(Preferences.PreservesPitch);
            ApplyStretchFilter();
        }
    }

    /// <summary>A pitch or tempo was asked for this song.</summary>
    public bool IsTargeted { get; private set; }

    /// <summary>How many semitones the current song sounds from its own pitch.</summary>
    public double HeardSemitones => PreservesPitch ? 0 : PlaybackTarget.Semitones(Rate);

    public void SetRate(double rate)
    {
        LeaveTarget();
        Rate = PlaybackSpeed.Clamped(rate);
        Player.SetSpeed(Rate);
        ApplyStretchFilter();
        Preferences.SetRate(Rate, Kind);
        Save();
    }

    /// <summary>0 … 1; also unmutes.</summary>
    public void SetVolume(double volume)
    {
        Preferences.Volume = Math.Clamp(volume, 0, 1);
        Player.SetVolume(Preferences.Volume);
        IsMuted = false;
        Player.SetMuted(false);
        Save();
    }

    public void ToggleMute()
    {
        IsMuted = !IsMuted;
        Player.SetMuted(IsMuted || _scrubbing);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool _scrubbing;

    /// <summary>Silent while the playhead is dragged; the sound comes back on release.</summary>
    public void SetScrubbing(bool scrubbing)
    {
        if (scrubbing == _scrubbing)
        {
            return;
        }
        _scrubbing = scrubbing;
        Player.SetMuted(IsMuted || scrubbing);
    }

    public void SetPreservesPitch(bool preserves)
    {
        LeaveTarget();
        Preferences.PreservesPitch = preserves;
        Player.SetPitchCorrection(preserves);
        ApplyStretchFilter();
        Save();
    }

    public void SetSegmentFades(bool fades)
    {
        Preferences.SegmentFades = fades;
        Save();
    }

    public void ToggleLoop()
    {
        Loops = !Loops;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>"Play next": continue with the next file at the end, remembered per kind.</summary>
    public void TogglePlaysNext()
    {
        AutoAdvance = !AutoAdvance;
        Preferences.SetPlaysNext(AutoAdvance, Kind);
        Save();
    }

    private void Save()
    {
        _preferencesFile.Write(Preferences.ToJson());
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SetTime(double seconds)
    {
        if (!double.IsFinite(seconds))
        {
            return;
        }
        CurrentTime = seconds;
        TimeChanged?.Invoke(this, seconds);
        if (StopTime is { } stop && IsPlaying && seconds >= stop)
        {
            // The end bracket: pause exactly there.
            Player.SetPaused(true);
            IsPlaying = false;
            Seek(stop, exact: true);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Stops and releases the file. The player itself goes once its video surface let go of it.</summary>
    public void Stop()
    {
        Player.Stop();
        IsPlaying = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Started -= OnStarted;
        Stopped -= OnStopped;
        Stopped?.Invoke(this);
        Player.Dispose();
    }
}
