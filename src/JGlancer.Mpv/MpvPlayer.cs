using System.Runtime.InteropServices;

namespace JGlancer.Mpv;

/// <summary>Why a file stopped playing.</summary>
public enum MpvEndReason
{
    EndOfFile,
    Stopped,
    Error,
}

/// <summary>
/// One libmpv instance (ARCHITECTURE §9 on Windows, W-32): local files only, no config files, scripts, on-screen
/// controls or key bindings of its own; it stays open at the end of a file (paused) so the app decides what comes
/// next. Events arrive on a background thread; subscribers marshal to their own thread. Idle (paused or stopped) it
/// sends nothing, so it costs no CPU.
/// </summary>
public sealed unsafe class MpvPlayer : IDisposable
{
    private const ulong TimePosId = 1;
    private const ulong DurationId = 2;
    private const ulong PauseId = 3;
    private const ulong EofId = 4;
    private const ulong VideoWidthId = 5;
    private const ulong VideoHeightId = 6;

    private readonly Thread _events;
    private nint _handle;
    private volatile bool _stopping;

    /// <param name="video">False for an audio-only player: no video output at all.</param>
    public MpvPlayer(bool video)
        : this(video, [])
    {
    }

    /// <summary>An audio player with <paramref name="extra"/> options on top (the waveform decoder).</summary>
    internal MpvPlayer(IEnumerable<(string Name, string Value)> extra)
        : this(false, extra)
    {
    }

    /// <summary>
    /// A headless instance for the encoding mode: no output devices of its own; <paramref name="withVideo"/> keeps
    /// the video track (a video segment), otherwise only audio is read.
    /// </summary>
    internal MpvPlayer(IEnumerable<(string Name, string Value)> extra, bool withVideo)
        : this(withVideo ? null : false, extra)
    {
    }

    /// <param name="video">True: a player with a video surface; false: audio only; null: headless (encoding).</param>
    private MpvPlayer(bool? video, IEnumerable<(string Name, string Value)> extra)
    {
        _handle = MpvNative.Create();
        if (_handle == 0)
        {
            throw new InvalidOperationException("mpv_create failed");
        }
        foreach (var (name, value) in Options(video).Concat(extra))
        {
            MpvNative.SetOptionString(_handle, name, value);
        }
        Check(MpvNative.Initialize(_handle), "mpv_initialize");
        MpvNative.ObserveProperty(_handle, TimePosId, "time-pos", MpvNative.FormatDouble);
        MpvNative.ObserveProperty(_handle, DurationId, "duration", MpvNative.FormatDouble);
        MpvNative.ObserveProperty(_handle, PauseId, "pause", MpvNative.FormatFlag);
        MpvNative.ObserveProperty(_handle, EofId, "eof-reached", MpvNative.FormatFlag);
        MpvNative.ObserveProperty(_handle, VideoWidthId, "dwidth", MpvNative.FormatInt64);
        MpvNative.ObserveProperty(_handle, VideoHeightId, "dheight", MpvNative.FormatInt64);
        _events = new Thread(EventLoop) { IsBackground = true, Name = "mpv events" };
        _events.Start();
    }

    /// <summary>
    /// Players made from now on play without sound (developer runs with <c>--data-dir</c>, tests; or the
    /// JGLANCER_SILENT environment variable).
    /// </summary>
    public static bool IsSilent { get; set; } = Environment.GetEnvironmentVariable("JGLANCER_SILENT") is { Length: > 0 };

    /// <summary>Whether libmpv is next to the app (scripts\deps.ps1).</summary>
    public static bool IsAvailable => MpvNative.IsAvailable;

    internal nint Handle => _handle;

    /// <summary>Playback position in seconds (background thread).</summary>
    public event Action<double>? TimeChanged;

    /// <summary>The file's duration in seconds, once known (background thread).</summary>
    public event Action<double>? DurationChanged;

    /// <summary>Paused (true) or playing (background thread).</summary>
    public event Action<bool>? PauseChanged;

    /// <summary>The file played to its end and stays there, paused (background thread).</summary>
    public event Action? EndReached;

    /// <summary>The file is loaded and playing or ready (background thread).</summary>
    public event Action? FileLoaded;

    /// <summary>Loading or playing stopped: <see cref="MpvEndReason.Error"/> when the file cannot be played.</summary>
    public event Action<MpvEndReason>? Ended;

    /// <summary>The video's display size changed (0 × 0 without video).</summary>
    public event Action<int, int>? VideoSizeChanged;

    private static IEnumerable<(string, string)> Options(bool? video)
    {
        yield return ("config", "no");
        yield return ("load-scripts", "no");
        yield return ("ytdl", "no");
        yield return ("osc", "no");
        yield return ("input-default-bindings", "no");
        yield return ("input-vo-keyboard", "no");
        yield return ("terminal", "no");
        yield return ("idle", "yes");
        // At the end the file stays loaded and paused; the app loops, advances or stops.
        yield return ("keep-open", "always");
        yield return ("audio-client-name", "jGlancer");
        if (IsSilent)
        {
            // Developer runs and tests: no sound device at all (playback still runs and reports time).
            yield return ("ao", "null");
        }
        yield return ("volume-max", "100");
        // Embedded cover art is shown by the app, not decoded as a video track.
        yield return ("audio-display", "no");
        // Subtitles (W-88): the file's own (its default or forced track) and files named like the video beside it.
        yield return ("sid", "auto");
        yield return ("sub-auto", "fuzzy");
        // No looking through the folder for a cover: the app has its own, and big folders slow every file's opening.
        yield return ("cover-art-auto", "no");
        if (video is null)
        {
            yield break;
        }
        if (video.Value)
        {
            // Frames go to the app's render context (MpvRenderContext); hardware decoding where it is safe.
            yield return ("vo", "libmpv");
            yield return ("hwdec", "auto-safe");
        }
        else
        {
            yield return ("vo", "null");
            yield return ("vid", "no");
        }
    }

    // MARK: Commands

    /// <summary>Opens <paramref name="path"/> at <paramref name="start"/> seconds; it starts playing unless <paramref name="paused"/>.</summary>
    public void Load(string path, bool paused = false, double start = 0)
    {
        // Tracks mixed for the last file mean nothing for this one.
        if (_mixedAudio.Count > 1)
        {
            _mixedAudio = [];
            CommandAsync("set", "lavfi-complex", "");
        }
        // Queued, not waited for: mpv's core may be closing the last file and its decoder, which must not hold up the
        // caller's (UI) thread; mpv runs these in order (W-100).
        CommandAsync("set", "pause", paused ? "yes" : "no");
        if (start > 0)
        {
            // loadfile <url> <flags> <index> <options>; index -1: no playlist position (mpv 0.38+).
            CommandAsync("loadfile", path, "replace", "-1", "start=" + Invariant(start));
        }
        else
        {
            CommandAsync("loadfile", path, "replace");
        }
    }

    public void Stop() => Command("stop");

    /// <summary>mpv writes its detailed log to <paramref name="path"/> (developer checks).</summary>
    public void SetLogFile(string path) => MpvNative.SetPropertyString(_handle, "log-file", path);

    public void SetPaused(bool paused) => SetFlag("pause", paused);

    /// <summary>Jumps to <paramref name="seconds"/>; <paramref name="exact"/> to the frame, else the nearest keyframe (scrubbing).</summary>
    public void Seek(double seconds, bool exact) =>
        CommandAsync("seek", Invariant(Math.Max(0, seconds)), exact ? "absolute+exact" : "absolute+keyframes");

    /// <summary>One video frame forward or back (pauses).</summary>
    public void Step(int frames)
    {
        for (var i = 0; i < Math.Abs(frames); i++)
        {
            Command(frames > 0 ? "frame-step" : "frame-back-step");
        }
    }

    public void SetSpeed(double rate) => SetDouble("speed", rate);

    /// <summary>The current file's tracks (W-88): video, audio and subtitle streams, embedded or beside it.</summary>
    public IReadOnlyList<MpvTrack> Tracks()
    {
        var tracks = new List<MpvTrack>();
        if (!long.TryParse(GetString("track-list/count"), out var count))
        {
            return tracks;
        }
        for (var i = 0; i < count; i++)
        {
            string? Field(string name) => GetString($"track-list/{i}/{name}") is { Length: > 0 } value ? value : null;
            if (!long.TryParse(Field("id"), out var id) || Field("type") is not { } type)
            {
                continue;
            }
            tracks.Add(new MpvTrack(id, type, Field("title"), Field("lang"), Field("codec"), Field("default") == "yes",
                Field("external") == "yes", long.TryParse(Field("demux-channel-count"), out var channels) ? (int)channels : 0));
        }
        return tracks;
    }

    private readonly Lock _readGate = new();

    /// <summary>
    /// A read of mpv off the caller's thread. While a file starts, mpv's core waits for the first frame to be drawn; a
    /// UI thread blocked in a read cannot draw it, and both stand still until mpv's 200 ms timeout (W-100). Null when the
    /// player is gone by then.
    /// </summary>
    public Task<T?> ReadAsync<T>(Func<MpvPlayer, T?> read) where T : class => Task.Run(() =>
    {
        lock (_readGate)
        {
            return _handle == 0 ? null : read(this);
        }
    });

    private IReadOnlyList<long> _mixedAudio = [];

    /// <summary>The audio tracks playing: one (mpv's own choice or the user's), or several mixed together.</summary>
    public IReadOnlyList<long> SelectedAudio =>
        _mixedAudio.Count > 1 ? _mixedAudio : long.TryParse(GetString("aid"), out var id) ? [id] : [];

    /// <summary>
    /// Plays <paramref name="ids"/>: one track as usual, or several at once mixed into one (W-88: mpv's filter graph
    /// <c>[aid1] [aid2] amix [ao]</c>, each at its own level).
    /// </summary>
    public void SelectAudio(IReadOnlyList<long> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count > 1)
        {
            var inputs = string.Concat(ids.Select(id => $"[aid{Invariant(id)}] "));
            _mixedAudio = [.. ids];
            MpvNative.SetPropertyString(_handle, "lavfi-complex", $"{inputs}amix=inputs={Invariant(ids.Count)}:normalize=0 [ao]");
            return;
        }
        var mixed = _mixedAudio.Count > 1;
        _mixedAudio = [];
        if (mixed)
        {
            MpvNative.SetPropertyString(_handle, "lavfi-complex", "");
        }
        if (ids is [var only])
        {
            MpvNative.SetPropertyString(_handle, "aid", Invariant(only));
        }
    }

    /// <summary>The subtitle track shown, or null when off.</summary>
    public long? SelectedSubtitle => long.TryParse(GetString("sid"), out var id) ? id : null;

    /// <summary>Shows subtitle track <paramref name="id"/>, or none.</summary>
    public void SelectSubtitle(long? id) =>
        MpvNative.SetPropertyString(_handle, "sid", id is { } shown ? Invariant(shown) : "no");

    /// <summary>
    /// HDR (W-87): mpv draws the video itself into <paramref name="window"/> (a child window of the app's) through
    /// Direct3D 11 with the display's colour space hinted, so HDR10 reaches an HDR display at full brightness; 0 goes
    /// back to the app's render context (the picture inside the UI, tone-mapped to SDR). Returns false when mpv refused.
    /// </summary>
    public bool UseOwnWindow(nint window)
    {
        if (window != 0)
        {
            MpvNative.SetOptionString(_handle, "wid", window.ToString(System.Globalization.CultureInfo.InvariantCulture));
            MpvNative.SetOptionString(_handle, "gpu-api", "d3d11");
            MpvNative.SetOptionString(_handle, "target-colorspace-hint", "yes");
            MpvNative.SetOptionString(_handle, "input-cursor", "no");
            return MpvNative.SetPropertyString(_handle, "vo", "gpu-next") >= 0;
        }
        var back = MpvNative.SetPropertyString(_handle, "vo", "libmpv") >= 0;
        MpvNative.SetOptionString(_handle, "wid", "-1");
        return back;
    }

    /// <summary>
    /// Zooms and pans the picture (mpv draws it, sharp at any zoom): <paramref name="zoomLog2"/> is log2 of the scale
    /// over fit; the pans are fractions of the scaled picture's size (positive: right, down).
    /// </summary>
    public void SetView(double zoomLog2, double panX, double panY)
    {
        SetDouble("video-zoom", zoomLog2);
        SetDouble("video-pan-x", panX);
        SetDouble("video-pan-y", panY);
    }

    /// <summary>0 … 1.</summary>
    public void SetVolume(double volume) => SetDouble("volume", Math.Clamp(volume, 0, 1) * 100);

    public void SetMuted(bool muted) => SetFlag("mute", muted);

    /// <summary>Keeps the pitch at speeds other than 1× (on), or plays like tape (off).</summary>
    public void SetPitchCorrection(bool on) => SetFlag("audio-pitch-correction", on);

    public double? Position => GetDouble("time-pos");

    public double? Duration => GetDouble("duration");

    /// <summary>A tag of the current file ("title", "artist", "album"), or null.</summary>
    public string? Tag(string key)
    {
        var value = GetString("metadata/by-key/" + key);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>The value of any property as text (diagnostics), or null.</summary>
    public string? GetString(string name)
    {
        var value = MpvNative.GetPropertyString(_handle, name);
        try
        {
            return MpvNative.Text(value);
        }
        finally
        {
            if (value is not null)
            {
                MpvNative.Free(value);
            }
        }
    }

    private void SetFlag(string name, bool value)
    {
        var flag = value ? 1 : 0;
        MpvNative.SetProperty(_handle, name, MpvNative.FormatFlag, &flag);
    }

    private void SetDouble(string name, double value) => MpvNative.SetProperty(_handle, name, MpvNative.FormatDouble, &value);

    private double? GetDouble(string name)
    {
        double value;
        return MpvNative.GetProperty(_handle, name, MpvNative.FormatDouble, &value) >= 0 ? value : null;
    }

    private static string Invariant(double value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private void Command(params string[] args) => Run(args, (handle, argv) => MpvNative.Command(handle, argv));

    /// <summary>
    /// Saves the current video frame at the video's own size (no zoom, subtitles or on-screen display) as an image;
    /// the format follows the extension (.png). False when it failed. Blocking: call it off the UI thread.
    /// </summary>
    public bool SaveFrame(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Run(["screenshot-to-file", path, "video"], (handle, argv) => MpvNative.Command(handle, argv)) >= 0;
    }

    private void CommandAsync(params string[] args) => Run(args, (handle, argv) => MpvNative.CommandAsync(handle, 0, argv));

    private delegate int Invoke(nint handle, byte** argv);

    /// <summary>Runs a command with its arguments as a null-terminated array of UTF-8 strings.</summary>
    private int Run(string[] args, Invoke invoke)
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
        var strings = new nint[args.Length];
        var argv = (byte**)NativeMemory.Alloc((nuint)(args.Length + 1), (nuint)sizeof(nint));
        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                strings[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
                argv[i] = (byte*)strings[i];
            }
            argv[args.Length] = null;
            return invoke(_handle, argv);
        }
        finally
        {
            foreach (var s in strings)
            {
                Marshal.FreeCoTaskMem(s);
            }
            NativeMemory.Free(argv);
        }
    }

    // MARK: Events

    private void EventLoop()
    {
        while (!_stopping)
        {
            var e = MpvNative.WaitEvent(_handle, -1);
            if (_stopping || e is null)
            {
                return;
            }
            switch (e->EventId)
            {
                case MpvNative.EventShutdown:
                    return;
                case MpvNative.EventFileLoaded:
                    FileLoaded?.Invoke();
                    break;
                case MpvNative.EventEndFile:
                    var end = (MpvNative.EndFileData*)e->Data;
                    var reason = end->Reason switch
                    {
                        MpvNative.EndFileError => MpvEndReason.Error,
                        MpvNative.EndFileEof => MpvEndReason.EndOfFile,
                        _ => MpvEndReason.Stopped,
                    };
                    Ended?.Invoke(reason);
                    break;
                case MpvNative.EventPropertyChange:
                    PropertyChanged(e->ReplyUserdata, (MpvNative.PropertyData*)e->Data);
                    break;
            }
        }
    }

    private void PropertyChanged(ulong id, MpvNative.PropertyData* property)
    {
        if (property->Format == MpvNative.FormatNone || property->Data is null)
        {
            if (id == VideoWidthId || id == VideoHeightId)
            {
                VideoSizeChanged?.Invoke(0, 0);
            }
            return;
        }
        switch (id)
        {
            case TimePosId:
                TimeChanged?.Invoke(*(double*)property->Data);
                break;
            case DurationId:
                DurationChanged?.Invoke(*(double*)property->Data);
                break;
            case PauseId:
                PauseChanged?.Invoke(*(int*)property->Data != 0);
                break;
            case EofId when *(int*)property->Data != 0:
                EndReached?.Invoke();
                break;
            case VideoWidthId:
            case VideoHeightId:
                long width = 0, height = 0;
                MpvNative.GetProperty(_handle, "dwidth", MpvNative.FormatInt64, &width);
                MpvNative.GetProperty(_handle, "dheight", MpvNative.FormatInt64, &height);
                VideoSizeChanged?.Invoke((int)width, (int)height);
                break;
        }
    }

    private static void Check(int result, string call)
    {
        if (result < 0)
        {
            throw new InvalidOperationException($"{call}: {MpvNative.Text(MpvNative.ErrorString(result))}");
        }
    }

    /// <summary>Stops the event thread, then destroys the instance (any render context must be disposed first).</summary>
    public void Dispose()
    {
        if (_handle == 0)
        {
            return;
        }
        _stopping = true;
        MpvNative.Wakeup(_handle);
        _events.Join();
        lock (_readGate)
        {
            MpvNative.TerminateDestroy(_handle);
            _handle = 0;
        }
    }

    /// <summary>The text of an mpv error code.</summary>
    public static string ErrorText(int error) => MpvNative.Text(MpvNative.ErrorString(error)) ?? error.ToString(System.Globalization.CultureInfo.InvariantCulture);

}
