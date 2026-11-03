using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using JGlancer.App.Viewer.Media;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.Preview;

/// <summary>
/// The preview of one file (macOS MiniPlayer), shared by the pane and Quick Preview. A click on the video toggles
/// pause; in Quick Preview a double-click toggles full screen (W-05). The video surface lives in the window that
/// shows it (<see cref="HostsSurface"/>; one at a time, W-41).
/// </summary>
internal sealed partial class MiniPlayer : UserControl
{
    /// <summary>A flat line while the waveform is read (the scrubber stays a waveform scrubber).</summary>

    private PreviewModel? _model;
    private MediaPlayback? _playback;
    private bool _isQuickPreview;
    private bool _hostsSurface;
    private bool _updatingVolume;
    private long _shownSecond = -1;

    public MiniPlayer()
    {
        InitializeComponent();
        FailedText.Text = L.T("Can't play this file.");
        PlayButton.Click += (_, _) =>
        {
            if (_model?.Frames is not null)
            {
                _model.ToggleFrames();
            }
            else
            {
                _model?.TogglePlay();
            }
        };
        SpeedButton.Click += (_, _) =>
        {
            if (_playback is { } playback)
            {
                SpeedMenu.Show(SpeedButton, playback);
            }
        };
        MuteButton.Click += (_, _) => _playback?.ToggleMute();
        FullScreenButton.Click += (_, _) => FullScreenRequested?.Invoke(this, EventArgs.Empty);
        Scrubber.Seek += (seconds, isFinal) =>
        {
            if (_model?.Frames is { } frames)
            {
                // A sequence: the timeline's "seconds" are its frames.
                _model.ShowFrame((int)Math.Min(Math.Floor(seconds), frames.Count - 1));
                return;
            }
            if (_model is { OwnsPlayback: true })
            {
                // Exact while dragging too: the frame under the pointer, not the nearest keyframe; silent meanwhile.
                _playback?.SetScrubbing(!isFinal);
                _playback?.Seek(seconds, exact: true);
            }
        };
        VolumeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == JGSlider.ValueProperty && !_updatingVolume)
            {
                _playback?.SetVolume(VolumeSlider.Value / 100);
            }
        };
        Stage.PointerPressed += OnStagePressed;
        Viewer.Media.SongTags.Read += path =>
        {
            if (_model?.Asset?.Path == path)
            {
                Update();
            }
        };
        // The waveform's look follows Settings at once.
        Settings.SettingsStore.Current.Changed += (_, change) =>
        {
            if (change.Old.Waveform != change.New.Waveform)
            {
                Update();
            }
        };
    }

    /// <summary>Quick Preview asks for full screen (the button or a double-click on the picture).</summary>
    public event EventHandler? FullScreenRequested;

    public PreviewModel? Model
    {
        get => _model;
        set
        {
            if (_model is not null)
            {
                _model.Changed -= OnModelChanged;
                _model.SurfaceMoved -= OnSurfaceMoved;
                _model.FrameChanged -= OnFrameChanged;
            }
            _model = value;
            if (_model is not null)
            {
                _model.Changed += OnModelChanged;
                _model.SurfaceMoved += OnSurfaceMoved;
                _model.FrameChanged += OnFrameChanged;
            }
            Update();
        }
    }

    /// <summary>The larger Quick Preview layout: bigger cover and waveform, heading title, volume and full screen.</summary>
    public bool IsQuickPreview
    {
        get => _isQuickPreview;
        set
        {
            _isQuickPreview = value;
            CoverFrame.Width = CoverFrame.Height = value ? JGMetrics.CoverArtSide : JGMetrics.MiniCoverSide;
            CoverGlyph.Width = CoverGlyph.Height = value ? JGMetrics.CoverGlyphSize : JGMetrics.TileGlyphSize;
            TitleText.Classes.Set("heading", value);
            TitleText.Classes.Set("body", !value);
            Update();
        }
    }

    /// <summary>This player shows the live video picture (the pane unless Quick Preview is open).</summary>
    public bool HostsSurface
    {
        get => _hostsSurface;
        set
        {
            _hostsSurface = value;
            UpdateSurface();
        }
    }

    /// <summary>Set by Quick Preview: the full-screen glyph and label.</summary>
    public bool IsFullScreen
    {
        get => _isFullScreen;
        set
        {
            _isFullScreen = value;
            Update();
        }
    }

    private bool _isFullScreen;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateSurface();
    }

    private void OnSurfaceMoved(object? sender, EventArgs e) => UpdateSurface();

    private void OnModelChanged(object? sender, EventArgs e) => Update();

    /// <summary>A sequence's frame: only the picture, the playhead, play / pause and the count change.</summary>
    private void OnFrameChanged(object? sender, EventArgs e)
    {
        if (_model?.Frames is not { } frames)
        {
            return;
        }
        PictureImage.Source = _model.Picture;
        Scrubber.Time = _model.FrameIndex + 0.5;
        var playing = _model.IsPlayingFrames;
        PlayGlyph.Data = playing ? JGCustomIcons.PauseFilled : JGCustomIcons.PlayFilled;
        Label(PlayButton, L.T(playing ? "Pause" : "Play"));
        TimeText.Text = FrameCount(_model.FrameIndex, frames.Count);
    }

    /// <summary>"12 / 90": the frame shown and the frames.</summary>
    private static string FrameCount(int index, int count) =>
        $"{(index + 1).ToString(System.Globalization.CultureInfo.CurrentCulture)} / {count.ToString(System.Globalization.CultureInfo.CurrentCulture)}";

    /// <summary>Puts the model's video surface here when this player hosts it, out of here otherwise.</summary>
    private void UpdateSurface()
    {
        var surface = _model?.Surface;
        foreach (var stale in SurfaceHost.Children.Where(child => child != surface).ToList())
        {
            SurfaceHost.Children.Remove(stale);
        }
        if (surface is null)
        {
            return;
        }
        var wanted = _hostsSurface && _model?.Asset?.Kind == MediaKind.Video;
        if (wanted && surface.Parent != SurfaceHost)
        {
            (surface.Parent as Panel)?.Children.Remove(surface);
            SurfaceHost.Children.Add(surface);
        }
        else if (!wanted && surface.Parent == SurfaceHost)
        {
            SurfaceHost.Children.Remove(surface);
        }
    }

    private void Update()
    {
        UpdateSurface();
        WatchPlayback(_model?.OwnsPlayback == true ? _model.Playback : null);
        if (_model?.Asset is not { } asset)
        {
            // Let go of the pictures too: the model disposes them right after.
            PictureImage.Source = null;
            CoverImage.Source = null;
            IsVisible = false;
            return;
        }
        IsVisible = true;
        var kind = asset.Kind;
        var failed = _model.IsFailed;
        var video = kind == MediaKind.Video;
        var audio = kind == MediaKind.Audio;
        var picture = _model.Picture;

        PictureImage.Source = audio ? null : picture;
        Stage.IsVisible = !audio && !failed;
        Stage.Aspect = Aspect(asset, picture);
        Placeholder.IsVisible = !audio && !failed && picture is null && _model.Surface?.Parent != SurfaceHost;
        Placeholder.Data = video ? JGIconData.Film : JGIconData.Image;
        if (_model.Surface is { } surface)
        {
            // The poster stays until the first frame is there.
            surface.Opacity = _playback?.VideoSize.Width > 0 ? 1 : 0;
        }

        AudioStage.IsVisible = audio && !failed;
        if (audio)
        {
            CoverImage.Source = _model.Cover;
            CoverGlyph.IsVisible = _model.IsCoverKnown && _model.Cover is null;
            // The tags Windows read (shared with the player) until the player has its own: the title and artist show at
            // once, and the subtitle keeps its line even when empty, so the cover never jumps.
            var known = Viewer.Media.SongTags.Of(asset.Path);
            TitleText.Text = _playback?.Title ?? known?.Title ?? Path.GetFileNameWithoutExtension(asset.Name);
            var subtitle = string.Join(" · ", new[] { _playback?.Artist ?? known?.Artist, _playback?.Album ?? known?.Album }.OfType<string>());
            SubtitleText.Text = subtitle.Length > 0 ? subtitle : "\u00A0";
            SubtitleText.IsVisible = true;
        }
        else
        {
            // Not a song: the last song's cover goes too (it is disposed once replaced).
            CoverImage.Source = null;
        }

        FailedMessage.IsVisible = failed;
        FailedGlyph.Data = video ? JGIconData.Film : JGIconData.AudioLines;

        var media = video || audio;
        Scrubber.IsVisible = media;
        // Settings → Appearance → Decor: the waveform or the plain timeline (W-100).
        var style = Settings.SettingsStore.Current.Settings.Waveform;
        var waveform = audio && style != Core.Settings.WaveformStyle.Timeline;
        var high = _isQuickPreview ? JGMetrics.WaveformHeight : JGMetrics.MiniWaveformHeight;
        Scrubber.Height = waveform ? high / 2 : JGMetrics.ControlHeightCompact;
        Scrubber.Waveform = waveform ? _model.Waveform ?? JGScrubber.Flat : null;
        Scrubber.Duration = _playback?.Duration ?? asset.Duration ?? 0;
        Scrubber.Time = _playback?.CurrentTime ?? 0;
        Scrubber.PlaybackRate = _playback?.MovingRate ?? 0;
        Controls.IsVisible = media || _isQuickPreview;
        PlayButton.IsVisible = TimeText.IsVisible = SpeedButton.IsVisible = media;
        PlayButton.IsEnabled = !failed && _playback is not null;
        var playing = _playback?.IsPlaying == true;
        PlayGlyph.Data = playing ? JGCustomIcons.PauseFilled : JGCustomIcons.PlayFilled;
        Label(PlayButton, L.T(playing ? "Pause" : "Play"));
        SpeedText.Text = SpeedMenu.Label(_playback?.Rate ?? _model.Playback?.Rate ?? 1);
        SpeedButton.IsEnabled = _playback is not null;
        UpdateTime();
        if (_model.Frames is { } sequence && !audio && !video)
        {
            // A folded sequence: a timeline of its frames, play / pause and "frame / frames".
            Scrubber.IsVisible = true;
            Scrubber.Waveform = null;
            Scrubber.Height = JGMetrics.ControlHeightCompact;
            Scrubber.Duration = sequence.Count;
            Scrubber.PlaybackRate = 0;
            Scrubber.Time = _model.FrameIndex + 0.5;
            Controls.IsVisible = PlayButton.IsVisible = PlayButton.IsEnabled = TimeText.IsVisible = true;
            SpeedButton.IsVisible = false;
            OnFrameChanged(this, EventArgs.Empty);
        }

        // Quick Preview has room for the volume; the side pane does not (macOS).
        MuteButton.IsVisible = VolumeSlider.IsVisible = _isQuickPreview && media;
        var muted = _playback is { } p && (p.IsMuted || p.Volume <= 0);
        MuteGlyph.Data = muted ? JGSolidIcons.VolumeOff : JGSolidIcons.Volume;
        Label(MuteButton, L.T(_playback?.IsMuted == true ? "Unmute" : "Mute"));
        _updatingVolume = true;
        VolumeSlider.Value = _playback is { } shown ? (shown.IsMuted ? 0 : shown.Volume * 100) : 0;
        _updatingVolume = false;
        FullScreenButton.IsVisible = _isQuickPreview && !audio;
        FullScreenGlyph.Data = _isFullScreen ? JGSolidIcons.FullScreenExit : JGSolidIcons.FullScreen;
        Label(FullScreenButton, L.T(_isFullScreen ? "Exit Full Screen" : "Full Screen"));
    }

    private static void Label(Control control, string label)
    {
        ToolTip.SetTip(control, label);
        AutomationProperties.SetName(control, label);
    }

    /// <summary>The picture's shape: the video's own size once known, else the library's, else the poster's.</summary>
    private double Aspect(Asset asset, Avalonia.Media.Imaging.Bitmap? picture)
    {
        if (_playback?.VideoSize is { Width: > 0, Height: > 0 } size)
        {
            return (double)size.Width / size.Height;
        }
        if (picture is { PixelSize: { Width: > 0, Height: > 0 } pixels })
        {
            return (double)pixels.Width / pixels.Height;
        }
        if (asset is { Width: > 0, Height: > 0 })
        {
            return (double)asset.Width.Value / asset.Height.Value;
        }
        return 16d / 9;
    }

    private void WatchPlayback(MediaPlayback? playback)
    {
        if (ReferenceEquals(playback, _playback))
        {
            return;
        }
        if (_playback is not null)
        {
            _playback.TimeChanged -= OnTimeChanged;
        }
        _playback = playback;
        _shownSecond = -1;
        if (_playback is not null)
        {
            _playback.TimeChanged += OnTimeChanged;
        }
    }

    private void OnTimeChanged(object? sender, double seconds)
    {
        Scrubber.Time = seconds;
        // The label changes once a second: no text layout per frame.
        var second = (long)Math.Floor(seconds);
        if (second != _shownSecond)
        {
            _shownSecond = second;
            UpdateTime();
        }
    }

    private void UpdateTime()
    {
        var elapsed = DurationFormat.Positional(_playback?.CurrentTime ?? 0) ?? "0:00";
        var duration = _playback?.Duration ?? _model?.Asset?.Duration;
        var total = duration is { } known ? DurationFormat.Positional(known) ?? "–:––" : "–:––";
        TimeText.Text = $"{elapsed} / {total}";
    }

    private void OnStagePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Stage).Properties.IsLeftButtonPressed || _model?.Asset is not { } asset)
        {
            return;
        }
        if (e.ClickCount == 2 && _isQuickPreview)
        {
            FullScreenRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.ClickCount == 1 && asset.Kind == MediaKind.Video)
        {
            _model.TogglePlay();
            e.Handled = true;
        }
    }
}
