using Avalonia.Automation;
using Avalonia.Controls;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.Viewer.Media;

/// <summary>The mini player's bar (W-68): shows <see cref="NowPlaying"/> and drives its player.</summary>
internal sealed partial class NowPlayingBar : UserControl
{
    private NowPlaying? _model;
    private MediaPlayback? _playback;
    private long _shownSecond = -1;
    private bool _updatingVolume;
    private Avalonia.Media.Imaging.Bitmap? _backdropOf;

    /// <summary>The backdrop's size: as the big player's, a tiny copy stretched over the bar blurs like frosted glass.</summary>
    private const int BackdropPixels = 8;

    public NowPlayingBar()
    {
        InitializeComponent();
        PlayButton.Click += (_, _) => _playback?.TogglePlay();
        PreviousButton.Click += (_, _) => _playback?.Previous();
        NextButton.Click += (_, _) => _playback?.Next();
        MuteButton.Click += (_, _) => _playback?.ToggleMute();
        SpeedButton.Click += (_, _) =>
        {
            if (_playback is { } playback)
            {
                SpeedMenu.Show(SpeedButton, playback);
            }
        };
        VolumeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == JGSlider.ValueProperty && !_updatingVolume)
            {
                _playback?.SetVolume(VolumeSlider.Value / 100);
            }
        };
        CloseButton.Click += (_, _) => _model?.Close();
        FolderButton.Click += (_, _) =>
        {
            if (_model?.Current?.AssetId is { } id)
            {
                FolderRequested?.Invoke(this, id);
            }
        };
        Scrubber.Seek += (seconds, isFinal) =>
        {
            // Silent while the playhead is dragged, as in the players.
            _playback?.SetScrubbing(!isFinal);
            _playback?.Seek(seconds, exact: true);
        };
    }

    /// <summary>The folder's name was clicked: show the song (its library id) in the library.</summary>
    public event EventHandler<long>? FolderRequested;

    public NowPlaying? Model
    {
        get => _model;
        set
        {
            if (_model is not null)
            {
                _model.Changed -= OnModelChanged;
            }
            _model = value;
            if (_model is not null)
            {
                _model.Changed += OnModelChanged;
            }
            Update();
        }
    }

    private void OnModelChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        WatchPlayback(_model?.Playback);
        IsVisible = _playback is not null;
        if (_model is not { } model || _playback is not { } playback)
        {
            CoverImage.Source = null;
            SetBackdrop(null);
            return;
        }
        var item = model.Current;
        TitleText.Text = playback.Title is { Length: > 0 } title
            ? playback.Artist is { Length: > 0 } artist ? $"{title} — {artist}" : title
            : item?.Name;
        ToolTip.SetTip(TitleText, TitleText.Text);
        var folder = model.FolderPath;
        FolderText.Text = folder is null ? null : Path.GetFileName(folder) is { Length: > 0 } name ? name : folder;
        var from = folder is null ? null : L.T("Playing from {0}", folder);
        ToolTip.SetTip(FolderButton, from);
        AutomationProperties.SetName(FolderButton, from);
        CoverImage.Source = model.Cover;
        SetBackdrop(model.Cover);
        PlayButton.IsEnabled = playback.Status != PlaybackStatus.Failed;
        PlayGlyph.Data = playback.IsPlaying ? JGCustomIcons.PauseFilled : JGCustomIcons.PlayFilled;
        Label(PlayButton, L.T(playback.IsPlaying ? "Pause" : "Play"));
        PreviousButton.IsEnabled = playback.Queue.Count > 0;
        NextButton.IsEnabled = playback.Queue.HasNext;
        MuteGlyph.Data = playback.IsMuted || playback.Volume <= 0 ? JGSolidIcons.VolumeOff : JGSolidIcons.Volume;
        Label(MuteButton, L.T(playback.IsMuted ? "Unmute" : "Mute"));
        _updatingVolume = true;
        VolumeSlider.Value = playback.IsMuted ? 0 : playback.Volume * 100;
        _updatingVolume = false;
        SpeedText.Text = SpeedMenu.Label(playback.Rate);
        Scrubber.Duration = playback.Duration ?? item?.Duration ?? 0;
        Scrubber.Time = playback.CurrentTime;
        Scrubber.PlaybackRate = playback.MovingRate;
        UpdateTime();
    }

    /// <summary>The cover's backdrop; made once per cover, shown as Settings → Appearance → Effects says.</summary>
    private void SetBackdrop(Avalonia.Media.Imaging.Bitmap? cover)
    {
        if (!ReferenceEquals(cover, _backdropOf))
        {
            _backdropOf = cover;
            (BackdropImage.Source as IDisposable)?.Dispose();
            BackdropImage.Source = cover?.CreateScaledBitmap(new Avalonia.PixelSize(BackdropPixels, BackdropPixels), Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
        }
        Backdrop.IsVisible = BackdropImage.Source is not null && Settings.SettingsStore.Current.Settings.CoverBackdrop;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Settings.SettingsStore.Current.Changed += OnSettingsChanged;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        Settings.SettingsStore.Current.Changed -= OnSettingsChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnSettingsChanged(object? sender, (Core.Settings.AppSettings Old, Core.Settings.AppSettings New) change) =>
        Backdrop.IsVisible = BackdropImage.Source is not null && change.New.CoverBackdrop;

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
        var duration = _playback?.Duration ?? _model?.Current?.Duration;
        var total = duration is { } known ? DurationFormat.Positional(known) ?? "–:––" : "–:––";
        TimeText.Text = $"{elapsed} / {total}";
    }

    private static void Label(Control control, string label)
    {
        ToolTip.SetTip(control, label);
        AutomationProperties.SetName(control, label);
    }
}
