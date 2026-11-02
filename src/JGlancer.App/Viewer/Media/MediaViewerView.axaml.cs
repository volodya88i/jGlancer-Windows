using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Core.Playback;
using JGlancer.Design;
using JGlancer.Platform.Windows;
using JGlancer.Media.Thumbnails;

namespace JGlancer.App.Viewer.Media;

/// <summary>
/// The video and audio viewer (macOS MediaViewerView). Keys (macOS MediaSurface): Space play / pause, ← → five
/// seconds, Alt+← → previous / next file, ↑ ↓ volume, , . a frame, [ ] speed, \ normal speed, F11 or double-click
/// full screen; Esc leaves full screen first, then the viewer. While a video plays, the chrome hides after
/// <see cref="JGMetrics.HudIdleSeconds"/> without pointer movement unless the pointer is on the controls.
/// </summary>
internal sealed partial class MediaViewerView : UserControl
{
    private readonly DispatcherTimer _chromeTimer;
    private readonly FilmstripRevealer _filmstrip;
    private MediaViewerModel? _model;
    private MpvVideoView? _video;
    private bool _isChromeVisible = true;
    private bool _isPointerOnControls;
    private bool _updatingVolume;
    private long _shownSecond = -1;
    private Point? _lastPointer;

    public MediaViewerView()
    {
        InitializeComponent();
        FailedText.Text = L.T("Can't play this file.");
        _chromeTimer = new DispatcherTimer(TimeSpan.FromSeconds(JGMetrics.HudIdleSeconds), DispatcherPriority.Normal, (_, _) => HideChrome());
        _chromeTimer.Stop();
        _filmstrip = new FilmstripRevealer(FilmstripToggle, FilmstripToggleGlyph, FilmstripPanel, Strip);

        TopBar.BackRequested += (_, _) =>
        {
            if (TopBar.IsAltBack && _model is { Kind: MediaKind.Audio } model && model.Count > 0)
            {
                MiniPlayerRequested?.Invoke(this, model);
            }
            else
            {
                LibraryRequested?.Invoke(this, EventArgs.Empty);
            }
        };
        PreviousButton.Click += (_, _) => Perform(MediaCommand.Previous);
        PlayButton.Click += (_, _) => Perform(MediaCommand.TogglePlay);
        NextButton.Click += (_, _) => Perform(MediaCommand.Next);
        SpeedButton.Click += (_, _) => ShowSpeedMenu();
        MuteButton.Click += (_, _) => _model?.Playback.ToggleMute();
        LoopButton.Click += (_, _) => _model?.Playback.ToggleLoop();
        PlayNextButton.Click += (_, _) => _model?.Playback.TogglePlaysNext();
        HdrButton.Click += (_, _) => ToggleHdr();
        AudioTracksButton.Click += (_, _) => ShowAudioMenu();
        SubtitlesButton.Click += (_, _) => ShowSubtitlesMenu();
        FullScreenButton.Click += (_, _) => FullScreenRequested?.Invoke(this, EventArgs.Empty);
        // Exact while dragging too: the picture shows the frame under the pointer, not the nearest keyframe.
        Scrubber.Seek += (seconds, isFinal) =>
        {
            if (_model is { } model)
            {
                // Silent while the playhead is dragged.
                model.Playback.SetScrubbing(!isFinal);
                model.Playback.Seek(seconds, exact: true);
            }
        };
        Scrubber.SegmentChanged += (range, edge, isFinal) =>
        {
            if (_model is { } model)
            {
                // The start bracket moves the playhead: silent while dragged. The end one keeps the sound.
                model.Playback.SetScrubbing(!isFinal && edge == JGSegmentEdge.Start);
                model.MoveSegment(new SegmentRange(range.Start, range.End), edge == JGSegmentEdge.Start, followPlayhead: !Scrubber.KeepsPlayhead);
            }
        };
        Scrubber.WindowChanged += ShowDetail;
        Scrubber.SegmentMenuRequested += ShowSegmentMenu;
        SegmentButton.Click += (_, _) => _model?.ToggleSegmentMode();
        SetUpFrameButton();
        // S toggles segment selection (the key shows in the tooltip).
        ToolTip.SetTip(SegmentButton, $"{L.T("Select a Segment")} (S)");
        FadesButton.Click += (_, _) => _model?.ToggleSegmentFades();
        ExportButton.AddHandler(PointerPressedEvent, OnExportPressed, RoutingStrategies.Tunnel);
        ExportButton.AddHandler(PointerMovedEvent, OnExportMoved, RoutingStrategies.Tunnel);
        ExportButton.AddHandler(PointerReleasedEvent, OnExportReleased, RoutingStrategies.Tunnel);
        VolumeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == JGSlider.ValueProperty && !_updatingVolume && _model is { } model)
            {
                model.Playback.SetVolume(VolumeSlider.Value / 100);
            }
        };
        // The swipe works anywhere over the audio stage area, not only on the cover (macOS).
        SetUpPager();
        PointerPressed += OnStagePressed;
        PointerMoved += OnStageMoved;
        AddHandler(PointerWheelChangedEvent, OnVideoWheel, Avalonia.Interactivity.RoutingStrategies.Bubble);
        PointerReleased += (_, e) =>
        {
            if (EndPan(e))
            {
                return;
            }
            if (_swiping)
            {
                _swiping = false;
                e.Pointer.Capture(null);
                _pager.End();
            }
        };
        ControlsPanel.PointerEntered += (_, _) => _isPointerOnControls = true;
        ControlsPanel.PointerExited += (_, _) => _isPointerOnControls = false;

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        // Space and Enter are the player's: a button that has the focus (one just clicked) must not click with them
        // as well (Space toggled the segment button back and forth). A click hands the keyboard back.
        AddHandler(KeyUpEvent, (_, e) => e.Handled |= e.Key is Key.Space or Key.Enter, RoutingStrategies.Tunnel);
        AddHandler(Button.ClickEvent, (_, e) =>
        {
            // Not from the speed button: its menu takes the keyboard.
            if (!ReferenceEquals(e.Source, SpeedButton))
            {
                Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Input);
            }
        }, RoutingStrategies.Bubble);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerExited += (_, _) => _filmstrip.PointerLeft();
    }

    /// <summary>Esc or "‹ Library".</summary>
    public event EventHandler? CloseRequested;

    /// <summary>"‹ Library" (it also loads the library when the app was opened for a file, D-65).</summary>
    public event EventHandler? LibraryRequested;

    /// <summary>Alt+"‹ Library" in the audio player: the song goes on in the mini player (macOS D-92).</summary>
    public event EventHandler<MediaViewerModel>? MiniPlayerRequested;

    /// <summary>F11, double-click or the button: the window toggles full screen.</summary>
    public event EventHandler? FullScreenRequested;

    /// <summary>A short message for the window's toast (a frame saved or copied).</summary>
    public event EventHandler<string>? MessageRequested;

    /// <summary>Set by the window; Esc leaves full screen before the viewer.</summary>
    public bool IsFullScreen
    {
        get => _isFullScreen;
        set
        {
            _isFullScreen = value;
            UpdateControls();
        }
    }

    private bool _isFullScreen;

    /// <summary>Completes once the video surface let go of the player (it may be disposed then).</summary>
    public Task PlayerReleased => Task.WhenAll(_video?.Released ?? Task.CompletedTask, _frameSave);

    public MediaViewerModel? Model
    {
        get => _model;
        set
        {
            if (_model is not null)
            {
                _model.Changed -= OnModelChanged;
                _model.Playback.Changed -= OnPlaybackChanged;
                _model.Playback.TimeChanged -= OnTimeChanged;
                _model.SegmentChanged -= OnSegmentChanged;
            }
            // mpv lets go of the HDR window before its player or surface changes.
            LeaveHdr();
            _model = value;
            Strip.Model = value;
            UpdatePagerAnimation();
            Surface.Children.Clear();
            _video = null;
            if (_model is { } model)
            {
                model.Changed += OnModelChanged;
                model.Playback.Changed += OnPlaybackChanged;
                model.Playback.TimeChanged += OnTimeChanged;
                model.SegmentChanged += OnSegmentChanged;
                if (model.Kind == MediaKind.Video)
                {
                    _video = new MpvVideoView(model.Playback.Player);
                    _video.RenderReady += (_, _) =>
                    {
                        model.Start();
                        ApplyHdrSoon();
                    };
                    _video.Unavailable += (_, _) =>
                    {
                        FailedMessage.IsVisible = true;
                        model.Start();
                    };
                    Surface.Children.Add(_video);
                }
                else
                {
                    model.Start();
                }
                var audio = model.Kind == MediaKind.Audio;
                AudioStage.IsVisible = audio;
                if (audio)
                {
                    // The audio player: a large waveform and the controls on the stage, no panel behind them.
                    ControlsPanel.Classes.Add("audio");
                    ControlsBlur.IsVisible = false;
                    ApplyWaveformStyle();
                }
                FailedGlyph.Data = model.Kind == MediaKind.Video ? JGIconData.Film : JGIconData.AudioLines;
                FullScreenButton.IsVisible = model.Kind == MediaKind.Video;
                FrameButton.IsVisible = model.Kind == MediaKind.Video;
                UpdateHdrButton();
                // The video player seeks to the end bracket while it is dragged (the frame under it shows).
                Scrubber.EndEdgeMovesPlayhead = model.Kind == MediaKind.Video;
            }
            OnModelChanged(this, EventArgs.Empty);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Settings.Motion.Enabled)
        {
            Chrome.Transitions = [Fade()];
            FilmstripPanel.Transitions = [Fade()];
        }
        _filmstrip.Attach();
        ShowChrome();
        Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Input);
        Settings.SettingsStore.Current.Changed += OnSettingsChanged;
        UpdatePagerAnimation();
        Scrubber.WheelZoomsAlone = Settings.SettingsStore.Current.Settings.TimelineWheelZoom;
    }

    /// <summary>Settings → Appearance and Decor apply while the player is open.</summary>
    private void OnSettingsChanged(object? sender, (Core.Settings.AppSettings Old, Core.Settings.AppSettings New) change)
    {
        UpdatePagerAnimation();
        Backdrop.IsVisible = (BackdropImage.Source ?? BackdropBelow.Source) is not null && change.New.CoverBackdrop;
        if (change.Old.Waveform != change.New.Waveform)
        {
            ApplyWaveformStyle();
        }
        Scrubber.WheelZoomsAlone = change.New.TimelineWheelZoom;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Settings.SettingsStore.Current.Changed -= OnSettingsChanged;
        LeaveHdr();
        DropRendered();
        _chromeTimer.Stop();
        _filmstrip.Detach();
        Cursor = null;
        base.OnDetachedFromVisualTree(e);
    }

    private static DoubleTransition Fade() => new()
    {
        Property = OpacityProperty,
        Duration = TimeSpan.FromSeconds(JGMetrics.FadeDuration),
        Easing = new CubicEaseOut(),
    };

    // MARK: Model

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (_model is not { } model)
        {
            return;
        }
        TopBar.Show(model.Current?.Name ?? "", model.Index, model.Count, model.Current?.AssetId);
        // Another file starts at fit.
        ResetZoom();
        _filmstrip.SetAvailable(model.Count > 1);
        if (model.Kind == MediaKind.Audio && model.Current is { } item && item.Path != _artworkFor)
        {
            _artworkFor = item.Path;
            _ = LoadArtworkAsync(model, item);
        }
        ShowChrome();
        UpdateControls();
    }

    private void OnPlaybackChanged(object? sender, EventArgs e)
    {
        UpdateControls();
        // Another file, or its picture is known now: HDR only for HDR video; its tracks.
        if (IsVideo)
        {
            ReadHdrVideo();
            ApplyHdr();
        }
        UpdateTracks();
        ShowChrome();
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
        if (_model is not { } model)
        {
            return;
        }
        var elapsed = DurationFormat.Positional(model.Playback.CurrentTime) ?? "0:00";
        var total = model.Playback.Duration is { } duration ? DurationFormat.Positional(duration) ?? "–:––" : "–:––";
        TimeText.Text = $"{elapsed} / {total}";
    }

    private void UpdateControls()
    {
        if (_model is not { } model)
        {
            return;
        }
        var playback = model.Playback;
        if (model.Kind == MediaKind.Audio)
        {
            // The player's tags once the file is open; until then the ones Windows read (the same the neighbor showed
            // while swiping), so the text does not change after a page; without tags the file name (macOS AudioStage).
            var known = model.Current is { } current ? Tags(current) : null;
            (TitleText.Text, SubtitleText.Text) = StageText(
                playback.Title ?? known?.Title, playback.Artist ?? known?.Artist, playback.Album ?? known?.Album, model.Current?.Name);
            AudioStage.IsVisible = !model.IsFailed;
        }
        Scrubber.Duration = playback.Duration ?? 0;
        Scrubber.Time = playback.CurrentTime;
        Scrubber.PlaybackRate = playback.MovingRate;
        UpdateTime();
        FailedMessage.IsVisible = model.IsFailed;
        PlayButton.IsEnabled = !model.IsFailed;
        PlayGlyph.Data = playback.IsPlaying ? JGCustomIcons.PauseFilled : JGCustomIcons.PlayFilled;
        var playLabel = L.T(playback.IsPlaying ? "Pause" : "Play");
        ToolTip.SetTip(PlayButton, playLabel);
        Avalonia.Automation.AutomationProperties.SetName(PlayButton, playLabel);
        NextButton.IsEnabled = playback.Queue.HasNext;
        SpeedText.Text = SpeedMenu.Label(playback.Rate);
        var muted = playback.IsMuted || playback.Volume <= 0;
        MuteGlyph.Data = muted ? JGSolidIcons.VolumeOff : JGSolidIcons.Volume;
        var muteLabel = $"{L.T(playback.IsMuted ? "Unmute" : "Mute")} (M)";
        ToolTip.SetTip(MuteButton, muteLabel);
        Avalonia.Automation.AutomationProperties.SetName(MuteButton, muteLabel);
        _updatingVolume = true;
        VolumeSlider.Value = playback.IsMuted ? 0 : playback.Volume * 100;
        _updatingVolume = false;
        LoopButton.Classes.Set("on", playback.Loops);
        PlayNextButton.Classes.Set("on", playback.AutoAdvance);
        UpdateSegmentControls();
        FullScreenGlyph.Data = _isFullScreen ? JGSolidIcons.FullScreenExit : JGSolidIcons.FullScreen;
        var fullScreenLabel = L.T(_isFullScreen ? "Exit Full Screen" : "Full Screen");
        ToolTip.SetTip(FullScreenButton, fullScreenLabel);
        Avalonia.Automation.AutomationProperties.SetName(FullScreenButton, fullScreenLabel);
    }

    private void ShowSpeedMenu()
    {
        if (_model is { } model)
        {
            SpeedMenu.Show(SpeedButton, model.Playback);
        }
    }

    // MARK: Audio

    private string? _artworkFor;
    private CancellationTokenSource? _artwork;

    /// <summary>
    /// The current song's cover art (its Windows thumbnail: embedded art) and backdrop, and its waveform, decoded in
    /// the background; both are dropped when another song becomes current.
    /// </summary>
    private async Task LoadArtworkAsync(MediaViewerModel model, PlaybackItem item)
    {
        _artwork?.Cancel();
        var cancel = new CancellationTokenSource();
        _artwork = cancel;
        SetCover(null, known: false);
        // The old song's wave settles into a line, and the next one rises from it once read (W-82).
        _peaks = null;
        ApplyWaveformStyle();
        var image = await model.Thumbnails.GetAsync(MediaViewerModel.Thumbnail(item, ThumbnailBucket.Large), ThumbnailPriority.Visible, cancel.Token);
        if (cancel.IsCancellationRequested)
        {
            return;
        }
        var cover = image is null ? null : await Task.Run(() => Library.ThumbnailCache.ToBitmap(image));
        if (cancel.IsCancellationRequested)
        {
            cover?.Dispose();
            return;
        }
        SetCover(cover, known: true);
        _samples = null;
        var samples = await JGlancer.Mpv.MpvAudioDecoder.SamplesAsync(item.Path, cancel.Token);
        if (!cancel.IsCancellationRequested && samples is not null)
        {
            // Kept (8 kHz mono, about 1 MB a minute) for finer peaks of a zoomed-in part.
            _samples = samples;
            var (lows, highs) = WaveformMath.Peaks(samples);
            _peaks = new JGWaveformPeaks(lows, highs);
            ApplyWaveformStyle();
        }
    }

    private JGWaveformPeaks? _peaks;

    /// <summary>Settings → Appearance → Decor: the waveform, half as high, or the plain timeline of video (W-71).</summary>
    private void ApplyWaveformStyle()
    {
        if (_model?.Kind != MediaKind.Audio)
        {
            return;
        }
        var style = Settings.SettingsStore.Current.Settings.Waveform;
        Scrubber.Height = style == Core.Settings.WaveformStyle.Timeline ? JGMetrics.ControlHeightCompact : JGMetrics.WaveformHeight / 2;
        Scrubber.Waveform = style == Core.Settings.WaveformStyle.Timeline ? null : _peaks ?? JGScrubber.Flat;
        if (style == Core.Settings.WaveformStyle.Timeline)
        {
            Scrubber.Detail = null;
        }
        else
        {
            ShowDetail(Scrubber.Shown);
        }
    }

    private void SetCover(Avalonia.Media.Imaging.Bitmap? cover, bool known)
    {
        (CoverImage.Source as IDisposable)?.Dispose();
        CoverImage.Source = cover;
        CoverGlyph.IsVisible = known && cover is null;
        // While the next cover loads the last backdrop stays, so the blend starts from it, not from black.
        if (known)
        {
            // A tiny copy stretched over the window blurs like frosted glass, without a blur effect redrawn every frame.
            SetBackdrop(cover?.CreateScaledBitmap(new PixelSize(BackdropPixels, BackdropPixels), Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality));
        }
    }

    /// <summary>
    /// Shows <paramref name="backdrop"/>: blended over <see cref="JGMetrics.BackdropCrossfadeDuration"/> from the last
    /// one (it fades out underneath while the new one fades in), or at once when the blend or motion is off.
    /// </summary>
    private void SetBackdrop(Avalonia.Media.Imaging.Bitmap? backdrop)
    {
        var settings = Settings.SettingsStore.Current.Settings;
        var blend = settings.BackdropCrossfade && Settings.Motion.Enabled && BackdropImage.Source is not null && !_backdropHandedOver;
        _backdropHandedOver = false;
        (BackdropBelow.Source as IDisposable)?.Dispose();
        BackdropBelow.Source = null;
        if (blend)
        {
            BackdropBelow.Source = BackdropImage.Source;
            Jump(BackdropBelow, 1);
            Fade(BackdropBelow, 0);
        }
        else
        {
            (BackdropImage.Source as IDisposable)?.Dispose();
        }
        BackdropImage.Source = backdrop;
        Jump(BackdropImage, blend ? 0 : 1);
        if (blend)
        {
            Fade(BackdropImage, 1);
        }
        Backdrop.IsVisible = (backdrop ?? BackdropBelow.Source) is not null && settings.CoverBackdrop;
    }

    /// <summary>Sets the opacity without a transition.</summary>
    private static void Jump(Control layer, double opacity)
    {
        layer.Transitions = null;
        layer.Opacity = opacity;
    }

    private static void Fade(Control layer, double opacity)
    {
        layer.Transitions =
        [
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromSeconds(JGMetrics.BackdropCrossfadeDuration),
                Easing = new CubicEaseInOut(),
            },
        ];
        // After the layout pass, so the jump above is not merged into the fade.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => layer.Opacity = opacity, Avalonia.Threading.DispatcherPriority.Render);
    }

    private readonly SwipePager _pager = new();
    private bool _swiping;
    private double _swipeLast;
    private int _neighborIndex = -1;
    private CancellationTokenSource? _neighborCover;

    /// <summary>The pager pages the song queue; the stage (and its neighbor) follow its offset.</summary>
    private void SetUpPager()
    {
        _pager.Host = () => TopLevel.GetTopLevel(this);
        _pager.CanPage = direction => _model is { } model && model.Playback.Queue.Select(model.Playback.Queue.Index + direction) is not null;
        _pager.Page = direction =>
        {
            if (_model is { } model)
            {
                HandOverBackdrop();
                _neighborIndex = -1;
                model.Playback.Select(model.Playback.Queue.Index + direction);
            }
        };
        _pager.Changed += (_, _) => ShowSwipe();
    }

    /// <summary>Settings → Appearance: the neighbor slides in while swiping (Beauty), or the song changes at once.</summary>
    private void UpdatePagerAnimation() =>
        _pager.IsAnimated = !IsVideo && Settings.Motion.Enabled && Settings.SettingsStore.Current.Settings.SwipeAnimation;

    private void ShowSwipe()
    {
        var offset = _pager.Offset;
        var neighbor = _pager.Neighbor;
        if (IsVideo)
        {
            // Videos page at once: nothing slides.
            return;
        }
        ((Avalonia.Media.TranslateTransform)AudioStage.RenderTransform!).X = offset;
        NeighborStage.IsVisible = neighbor != 0;
        BlendBackdrop(neighbor == 0 || _pager.Width <= 0 ? 0 : Math.Min(1, Math.Abs(offset) / _pager.Width));
        if (neighbor == 0 || _model is not { } model)
        {
            return;
        }
        ((Avalonia.Media.TranslateTransform)NeighborStage.RenderTransform!).X = offset + neighbor * _pager.Width;
        var index = model.Playback.Queue.Index + neighbor;
        if (index != _neighborIndex && index >= 0 && index < model.Playback.Queue.Items.Count)
        {
            _neighborIndex = index;
            var item = model.Playback.Queue.Items[index];
            var tags = Tags(item);
            (NeighborTitle.Text, NeighborSubtitle.Text) = StageText(tags?.Title, tags?.Artist, tags?.Album, item.Name);
            (NeighborCover.Source as IDisposable)?.Dispose();
            NeighborCover.Source = null;
            NeighborGlyph.IsVisible = true;
            SetNeighborBackdrop(null, known: false);
            _ = LoadNeighborCoverAsync(model, item);
        }
    }

    /// <summary>The stage's title and "artist · album"; the file name when there is no title.</summary>
    private static (string Title, string Subtitle) StageText(string? title, string? artist, string? album, string? name) =>
        (title ?? Path.GetFileNameWithoutExtension(name ?? ""), string.Join(" · ", new[] { artist, album }.OfType<string>()));

    private readonly Dictionary<string, Platform.Windows.AudioTags.Tags?> _tags = [];

    /// <summary>
    /// The tags Windows knows for a song, read once off the UI thread; null until then (the stage refreshes when they
    /// arrive). Songs come in a queue of a folder, so the cache stays small.
    /// </summary>
    private Platform.Windows.AudioTags.Tags? Tags(PlaybackItem item)
    {
        if (_tags.TryGetValue(item.Path, out var tags))
        {
            return tags;
        }
        _tags[item.Path] = null;
        _ = Task.Run(() => Platform.Windows.AudioTags.Read(item.Path)).ContinueWith(read =>
        {
            _tags[item.Path] = read.Result;
            UpdateControls();
            if (NeighborStage.IsVisible)
            {
                // The neighbor shown while swiping: its text again, now with the tags.
                _neighborIndex = -1;
                ShowSwipe();
            }
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.FromCurrentSynchronizationContext());
        return null;
    }

    private async Task LoadNeighborCoverAsync(MediaViewerModel model, PlaybackItem item)
    {
        _neighborCover?.Cancel();
        var cancel = new CancellationTokenSource();
        _neighborCover = cancel;
        var image = await model.Thumbnails.GetAsync(MediaViewerModel.Thumbnail(item, ThumbnailBucket.Large), ThumbnailPriority.Visible, cancel.Token);
        var cover = image is null || cancel.IsCancellationRequested ? null : await Task.Run(() => Library.ThumbnailCache.ToBitmap(image));
        if (cancel.IsCancellationRequested)
        {
            cover?.Dispose();
            return;
        }
        NeighborCover.Source = cover;
        NeighborGlyph.IsVisible = cover is null;
        SetNeighborBackdrop(cover?.CreateScaledBitmap(new PixelSize(BackdropPixels, BackdropPixels), Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality), known: true);
    }

    /// <summary>True once the neighbor's cover is known (it may have none: then the backdrop fades out as the swipe goes).</summary>
    private bool _neighborBackdropKnown;

    /// <summary>The swipe already blended to the new song's backdrop: when its cover arrives it replaces the picture without a second blend.</summary>
    private bool _backdropHandedOver;

    private void SetNeighborBackdrop(Avalonia.Media.Imaging.Bitmap? backdrop, bool known)
    {
        (BackdropNeighbor.Source as IDisposable)?.Dispose();
        BackdropNeighbor.Source = backdrop;
        _neighborBackdropKnown = known;
        ShowSwipeBlend();
    }

    private double _swipeBlend;

    /// <summary>
    /// The backdrop as far as the swipe has gone (0 the current song's, 1 the neighbor's), when the blend is on
    /// (Settings → Appearance → Effects) and the neighbor's cover is known.
    /// </summary>
    private void BlendBackdrop(double progress)
    {
        _swipeBlend = progress;
        ShowSwipeBlend();
    }

    private void ShowSwipeBlend()
    {
        var settings = Settings.SettingsStore.Current.Settings;
        var progress = settings.BackdropCrossfade && settings.CoverBackdrop && _neighborBackdropKnown ? _swipeBlend : 0;
        if (progress > 0)
        {
            // Not while a timed blend runs: the swipe drives the layers now.
            BackdropBelow.Transitions = null;
            BackdropBelow.Opacity = 0;
            BackdropImage.Transitions = null;
        }
        BackdropNeighbor.Opacity = BackdropNeighbor.Source is null ? 0 : progress;
        // Toward a song without a cover the current backdrop fades out instead.
        if (progress > 0 || BackdropImage.Transitions is null)
        {
            BackdropImage.Opacity = BackdropNeighbor.Source is null ? 1 - progress : 1;
        }
        Backdrop.IsVisible = settings.CoverBackdrop && (BackdropImage.Source ?? BackdropNeighbor.Source ?? BackdropBelow.Source) is not null;
    }

    /// <summary>The swipe completed: the neighbor's backdrop (fully shown by now) becomes the current one.</summary>
    private void HandOverBackdrop()
    {
        if (_swipeBlend <= 0 || !_neighborBackdropKnown || !Settings.SettingsStore.Current.Settings.BackdropCrossfade)
        {
            return;
        }
        (BackdropImage.Source as IDisposable)?.Dispose();
        BackdropImage.Source = BackdropNeighbor.Source;
        BackdropImage.Opacity = 1;
        BackdropNeighbor.Source = null;
        BackdropNeighbor.Opacity = 0;
        _neighborBackdropKnown = false;
        _swipeBlend = 0;
        _backdropHandedOver = true;
    }

    /// <summary>A drag across the stage turns to the next or previous song once it is long enough (macOS instant swipe).</summary>
    private void OnStagePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_model is not null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !e.Handled && e.ClickCount == 1
            && e.Source is Visual source && IsOverPicture(source) && !BeginPan(e))
        {
            _swiping = true;
            _pager.Width = Bounds.Width;
            _pager.Begin();
            _swipeLast = e.GetPosition(this).X;
            e.Pointer.Capture(this);
        }
    }

    private void OnStageMoved(object? sender, PointerEventArgs e)
    {
        if (MovePan(e) || !_swiping)
        {
            return;
        }
        var x = e.GetPosition(this).X;
        _pager.Move(x - _swipeLast);
        _swipeLast = x;
    }

    /// <summary>Side of the backdrop's tiny copy of the cover.</summary>
    private const int BackdropPixels = 8;

    // MARK: Segment

    private short[]? _samples;
    private string? _exportFolder;
    private (SegmentExportRequest Request, Task<string?> File)? _rendered;
    private CancellationTokenSource? _renderCancel;
    private DispatcherTimer? _renderDelay;
    private PointerPressedEventArgs? _exportPress;
    private Point _exportPressPoint;

    private void OnSegmentChanged(object? sender, EventArgs e)
    {
        UpdateSegmentControls();
        // The rendered file follows the segment a moment after it stops changing.
        _renderDelay ??= new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) =>
        {
            _renderDelay!.Stop();
            _ = Rendered();
        });
        _renderDelay.Stop();
        if (_model?.ExportRequest is not null)
        {
            _renderDelay.Start();
        }
    }

    private void UpdateSegmentControls()
    {
        if (_model is not { } model)
        {
            return;
        }
        var segment = model.Segment;
        Scrubber.Segment = segment is { } s ? new JGTimeRange(s.Start, s.End) : null;
        SegmentButton.Classes.Set("on", segment is not null);
        SegmentButton.IsEnabled = !model.IsFailed && (model.Playback.Duration ?? 0) > 0;
        SegmentLabel.IsVisible = segment is not null;
        SegmentLabel.Text = segment is { } shown ? Segments.Label(shown) : "";
        FadesButton.IsVisible = segment is not null && model.Kind == MediaKind.Audio;
        FadesButton.Classes.Set("on", model.Playback.Preferences.SegmentFades);
        ExportButton.IsVisible = segment is not null;
        ExportButton.IsEnabled = model.CanExportSegment;
    }

    /// <summary>The segment rendered to our temporary folder (started now if it is not already), or null.</summary>
    private Task<string?> Rendered()
    {
        if (_model?.ExportRequest is not { } request)
        {
            return Task.FromResult<string?>(null);
        }
        if (_rendered is { } done && done.Request == request)
        {
            return done.File;
        }
        _renderCancel?.Cancel();
        var cancel = new CancellationTokenSource();
        _renderCancel = cancel;
        _exportFolder ??= Platform.Windows.SegmentExporter.TemporaryFolder();
        var folder = Path.Combine(_exportFolder, Guid.NewGuid().ToString("N"));
        var file = Render(request, folder, cancel.Token);
        _rendered = (request, file);
        return file;

        static async Task<string?> Render(SegmentExportRequest request, string folder, CancellationToken cancellation)
        {
            try
            {
                Directory.CreateDirectory(folder);
                var target = Path.Combine(folder, Segments.SuggestedName(request));
                await Platform.Windows.SegmentExporter.ExportAsync(request, target, cancellation);
                return target;
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or NotSupportedException
                or System.Runtime.InteropServices.COMException or OperationCanceledException)
            {
                return null;
            }
        }
    }

    /// <summary>Forgets rendered segments and removes our temporary folder (viewer closed).</summary>
    private void DropRendered()
    {
        _renderCancel?.Cancel();
        _renderDelay?.Stop();
        _rendered = null;
        if (_exportFolder is { } folder)
        {
            _exportFolder = null;
            // Drag targets may still be reading the file: give them a minute.
            _ = Task.Delay(TimeSpan.FromMinutes(1)).ContinueWith(_ => Platform.Windows.SegmentExporter.RemoveTemporaryFolder(folder), TaskScheduler.Default);
        }
    }

    /// <summary>A press on the export button: a drag offers the file (rendered by then, at most a minute's wait), a click saves it.</summary>
    private void OnExportPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(ExportButton).Properties.IsLeftButtonPressed || _model?.ExportRequest is null)
        {
            return;
        }
        _exportPress = e;
        _exportPressPoint = e.GetPosition(ExportButton);
        _ = Rendered();
        e.Handled = true;
    }

    private async void OnExportMoved(object? sender, PointerEventArgs e)
    {
        if (_exportPress is not { } press || Vector.Distance(e.GetPosition(ExportButton), _exportPressPoint) < 4)
        {
            return;
        }
        _exportPress = null;
        var file = await Rendered().WaitAsync(TimeSpan.FromMinutes(1)).ConfigureAwait(true);
        if (file is null || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage
            || await storage.TryGetFileFromPathAsync(new Uri(file)) is not { } item)
        {
            return;
        }
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateFile(item));
        // Copy only: a drop on Explorer must not move the file out of our folder while it is in use.
        await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Copy);
    }

    private async void OnExportReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_exportPress is null)
        {
            return;
        }
        _exportPress = null;
        e.Handled = true;
        await SaveAsAsync();
    }

    /// <summary>Save As (D-74): a file it replaces goes to the Recycle Bin first, never overwritten.</summary>
    private async Task SaveAsAsync()
    {
        if (_model?.ExportRequest is not { } request || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }
        var name = Segments.SuggestedName(request);
        var extension = Path.GetExtension(name).TrimStart('.');
        var chosen = await storage.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            SuggestedFileName = name,
            DefaultExtension = extension,
            ShowOverwritePrompt = true,
            FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType(extension.ToUpperInvariant()) { Patterns = ["*." + extension] }],
        });
        if (chosen?.TryGetLocalPath() is not { } target)
        {
            return;
        }
        var rendered = await Rendered();
        if (rendered is null || (File.Exists(target) && !Platform.Windows.RecycleBin.Send(target)))
        {
            return;
        }
        File.Copy(rendered, target);
    }

    private void ShowSegmentMenu(PointerPressedEventArgs e)
    {
        var save = new MenuItem { Header = L.T("Save As…") };
        save.Click += async (_, _) => await SaveAsAsync();
        var menu = new MenuFlyout { Placement = PlacementMode.Pointer };
        menu.Items.Add(save);
        menu.ShowAt(Scrubber, showAtPointer: true);
    }

    /// <summary>Zoomed into audio: finer peaks of the shown part and as much again on each side (D-73), from the kept samples.</summary>
    private void ShowDetail(JGTimeRange shown)
    {
        if (_samples is not { } samples || _model is not { Kind: MediaKind.Audio } model || model.Playback.Duration is not { } duration
            || Settings.SettingsStore.Current.Settings.Waveform == Core.Settings.WaveformStyle.Timeline
            || shown.Length >= duration * 0.5)
        {
            Scrubber.Detail = null;
            return;
        }
        var from = Math.Max(0, shown.Start - shown.Length);
        var to = Math.Min(duration, shown.End + shown.Length);
        var rate = JGlancer.Mpv.MpvAudioDecoder.SamplesPerSecond;
        var first = Math.Clamp((int)(from * rate), 0, samples.Length);
        var last = Math.Clamp((int)(to * rate), first, samples.Length);
        if (last - first < 2)
        {
            return;
        }
        var (lows, highs) = WaveformMath.Peaks(samples.AsSpan(first, last - first), WaveformMath.Buckets * 3);
        Scrubber.Detail = new JGWaveformDetail(new JGTimeRange(from, to), new JGWaveformPeaks(lows, highs));
    }

    // MARK: Input

    /// <summary>A keyboard media key (W-84): as the player's own button.</summary>
    public void PerformMediaKey(MediaCommand command) => Perform(command);

    private void Perform(MediaCommand command)
    {
        if (command == MediaCommand.ToggleFullScreen)
        {
            FullScreenRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (command is MediaCommand.SaveFrame or MediaCommand.CopyFrame)
        {
            _ = SaveFrameAsync(copy: command == MediaCommand.CopyFrame);
        }
        else
        {
            _model?.Perform(command);
        }
        ShowChrome();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+0: a zoomed video back to fit (as the photo viewer).
        if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.D0 or Key.NumPad0 && IsVideo && IsZoomed)
        {
            ResetZoom();
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            return;
        }
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        MediaCommand? command = e.Key switch
        {
            Key.Space => MediaCommand.TogglePlay,
            Key.Left => alt ? MediaCommand.Previous : MediaCommand.SeekBack,
            Key.Right => alt ? MediaCommand.Next : MediaCommand.SeekForward,
            Key.Up => MediaCommand.VolumeUp,
            Key.Down => MediaCommand.VolumeDown,
            Key.OemComma => MediaCommand.StepBack,
            Key.OemPeriod => MediaCommand.StepForward,
            Key.OemOpenBrackets => MediaCommand.Slower,
            Key.OemCloseBrackets => MediaCommand.Faster,
            Key.OemPipe or Key.OemBackslash => MediaCommand.ResetSpeed,
            Key.F11 when _model?.Kind == MediaKind.Video => MediaCommand.ToggleFullScreen,
            Key.S when !alt && SegmentButton.IsEnabled => MediaCommand.ToggleSegment,
            Key.P when !alt && FrameButton.IsVisible => e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? MediaCommand.CopyFrame : MediaCommand.SaveFrame,
            Key.M when !alt => MediaCommand.ToggleMute,
            _ => null,
        };
        if (command is { } run)
        {
            Perform(run);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Esc leaves segment selection first, then full screen, then the viewer.
            if (_model is { IsSelectingSegment: true } selecting)
            {
                selecting.EndSegmentMode();
            }
            else if (_isFullScreen)
            {
                FullScreenRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
            e.Handled = true;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ShowChrome();
        // A double-click on the picture toggles full screen (not on the controls).
        if (e.ClickCount == 2 && _model?.Kind == MediaKind.Video && e.Source is Visual source
            && !ControlsPanel.IsVisualAncestorOf(source) && !TopBar.IsVisualAncestorOf(source) && !FilmstripArea.IsVisualAncestorOf(source))
        {
            FullScreenRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        if (_lastPointer == point)
        {
            return;
        }
        _lastPointer = point;
        Cursor = null;
        ShowChrome();
        var bandTop = Bounds.Height - ControlsPanel.Bounds.Height - JGMetrics.FilmstripHeight - JGMetrics.FilmstripRevealMargin;
        _filmstrip.Pointer(point.Y >= bandTop);
    }

    // MARK: Chrome

    /// <summary>Shows the chrome; while a video plays it hides again after a short idle.</summary>
    private void ShowChrome()
    {
        if (!_isChromeVisible)
        {
            _isChromeVisible = true;
            Chrome.Opacity = 1;
            Chrome.IsHitTestVisible = true;
        }
        _chromeTimer.Stop();
        if (_model is { Kind: MediaKind.Video } model && model.Playback.IsPlaying)
        {
            _chromeTimer.Start();
        }
    }

    private void HideChrome()
    {
        _chromeTimer.Stop();
        // HDR: the chrome sits beside the picture, not over it, and stays.
        if (IsHdr || _isPointerOnControls || _filmstrip.KeepsChrome ||_model is not { Kind: MediaKind.Video } model || !model.Playback.IsPlaying)
        {
            return;
        }
        _isChromeVisible = false;
        Chrome.Opacity = 0;
        Chrome.IsHitTestVisible = false;
        Cursor = new Cursor(StandardCursorType.None);
    }
}
