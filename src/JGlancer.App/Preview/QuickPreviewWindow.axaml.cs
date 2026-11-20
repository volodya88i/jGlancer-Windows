using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using JGlancer.App.Actions;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.Preview;

/// <summary>
/// Quick Preview (macOS QuickPreviewController, FEATURES §3): a window over the library that opens without taking the
/// keyboard, so the grid's arrows keep moving the selection and the preview follows. Space or Esc close it (the grid
/// passes them, or its own keys when it was clicked), Enter or a click on the video toggles pause, ← → ↑ ↓ move the
/// library's selection, F11 or a double-click toggle full screen (W-05); Space and Esc leave full screen first.
/// Closing only hides it; it keeps its place for the next time. Its ⓘ button (or the Get Info shortcut) opens Get
/// Info of the file shown, which then follows the file until it is closed.
/// </summary>
internal sealed partial class QuickPreviewWindow : Window
{
    /// <summary>Side of the backdrop's tiny copy of the cover (as the audio viewer).</summary>
    private const int BackdropPixels = 8;

    private readonly PreviewModel _preview;
    private Avalonia.Media.Imaging.Bitmap? _backdropFor;
    private InfoWindow? _info;
    private Asset? _infoFor;

    // The XAML loader needs a parameterless constructor; the app always passes the model.
    public QuickPreviewWindow()
        : this(null!)
    {
    }

    public QuickPreviewWindow(PreviewModel preview)
    {
        InitializeComponent();
        // Settings → Appearance: the blurred cover follows a change at once.
        Settings.SettingsStore.Current.Changed += (_, change) => Backdrop.IsVisible = BackdropImage.Source is not null && change.New.CoverBackdrop;
        _preview = preview;
        JGWindowChrome.Apply(this);
        // On top of every window (W-90): it stays in sight while another app has the focus; its pin turns that off.
        ApplyOnTop();
        PinButton.Click += (_, _) => Settings.SettingsStore.Current.Update(s => s with { QuickPreviewOnTop = !s.QuickPreviewOnTop });
        Settings.SettingsStore.Current.Changed += (_, change) =>
        {
            if (change.Old.QuickPreviewOnTop != change.New.QuickPreviewOnTop)
            {
                ApplyOnTop();
            }
        };
        Player.IsQuickPreview = true;
        // Never narrower than the whole controls row: nothing hides or overlaps (macOS MAC-021).
        Player.LayoutUpdated += (_, _) => KeepControlsRowWidth();
        Player.OverlayChanged += (_, _) => ApplyOverlay();
        Player.BarsShownChanged += (_, shown) =>
        {
            if (Player.IsOverlay)
            {
                NameBar.Opacity = shown ? 1 : 0;
                NameBar.IsHitTestVisible = shown;
            }
        };
        PointerExited += (_, _) => StartPhotoHide();
        PointerEntered += (_, _) => ShowNameBar();
        PointerMoved += (_, _) => ShowNameBar();
        Player.FullScreenRequested += (_, _) => ToggleFullScreen();
        CloseButton.Click += (_, _) => Dismiss();
        InfoButton.Click += (_, _) => ShowInfo();
        NameRow.SizeChanged += (_, _) => FitTitle();
        OpenButton.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        if (preview is not null)
        {
            // The video surface is here only while Quick Preview is open (set before the player hears of a move).
            Player.HostsSurface = preview.IsQuickPreviewOpen;
            preview.SurfaceMoved += (_, _) => Player.HostsSurface = preview.IsQuickPreviewOpen;
            Player.Model = preview;
            preview.Changed += (_, _) => Update();
        }
    }

    private void KeepControlsRowWidth()
    {
        var row = Player.ControlsRowWidth;
        if (row <= 0 || WindowState != WindowState.Normal)
        {
            return;
        }
        var needed = Math.Ceiling(Math.Max(JGMetrics.PreviewPaneMinWidth, row + JGInsets.Preview.Left + JGInsets.Preview.Right));
        if (Math.Abs(needed - MinWidth) >= 1)
        {
            MinWidth = needed;
            if (Bounds.Width < needed)
            {
                Width = needed;
            }
        }
    }

    /// <summary>The library may really close it (the app is quitting); otherwise closing hides it.</summary>
    public bool AllowsClose { get; set; }

    /// <summary>It went away by its ×, a key or Alt+F4.</summary>
    public event EventHandler? Dismissed;

    /// <summary>Open in Viewer: the file shown opens in the full viewer (and Quick Preview goes away).</summary>
    public event EventHandler? OpenRequested;

    /// <summary>An arrow pressed in it: the library's selection moves.</summary>
    public event EventHandler<Key>? ArrowPressed;

    public bool IsFullScreen => WindowState == WindowState.FullScreen;

    public void ToggleFullScreen()
    {
        WindowState = IsFullScreen ? WindowState.Normal : WindowState.FullScreen;
    }

    /// <summary>Leaves full screen first, then hides.</summary>
    public void Dismiss()
    {
        if (IsFullScreen)
        {
            WindowState = WindowState.Normal;
        }
        else if (IsVisible && WindowState == WindowState.Normal)
        {
            LastFrame = new WindowFrame(Position.X, Position.Y, Bounds.Width, Bounds.Height);
        }
        _info?.Close();
        Hide();
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Where it was when it last went away windowed, to be remembered.</summary>
    public WindowFrame? LastFrame { get; private set; }

    /// <summary>Puts it where it was last if that is still on a screen, else centered over <paramref name="owner"/>.</summary>
    public void Place(WindowFrame? frame, Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (frame is { Width: > 0, Height: > 0 } && Screens.All.Any(s => s.WorkingArea.Contains(new PixelPoint(frame.X + 1, frame.Y + 1))))
        {
            Width = Math.Max(frame.Width, MinWidth);
            Height = Math.Max(frame.Height, MinHeight);
            Position = new PixelPoint(frame.X, frame.Y);
            return;
        }
        var scaling = owner.RenderScaling;
        var center = owner.Position + new PixelPoint((int)(owner.Bounds.Width * scaling / 2), (int)(owner.Bounds.Height * scaling / 2));
        Position = center - new PixelPoint((int)(Width * scaling / 2), (int)(Height * scaling / 2));
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (!AllowsClose)
        {
            e.Cancel = true;
            Dismiss();
        }
    }

    /// <summary>Above every window while the pin is on; the pin in the accent then.</summary>
    private void ApplyOnTop()
    {
        var onTop = Settings.SettingsStore.Current.Settings.QuickPreviewOnTop;
        Topmost = onTop;
        PinGlyph[!JGIcon.ForegroundProperty] = PinGlyph.GetResourceObservable((onTop ? JGColor.AccentText : JGColor.TextSecondary).ResourceKey).ToBinding();
        var label = Core.Localization.L.T(onTop ? "Keep on Top: On" : "Keep on Top: Off");
        ToolTip.SetTip(PinButton, label);
        Avalonia.Automation.AutomationProperties.SetName(PinButton, label);
    }

    /// <summary>
    /// The name shows only when it fits between the buttons' room at both sides; otherwise it hides, and the Get Info
    /// button's tooltip names the file (owner, 0.6.1: a narrow window had the name over the buttons).
    /// </summary>
    private void FitTitle()
    {
        TitlePanel.IsVisible = true;
        TitlePanel.Measure(Size.Infinity);
        var room = NameRow.Bounds.Width - TitlePanel.Margin.Left - TitlePanel.Margin.Right;
        var fits = NameRow.Bounds.Width <= 0 || TitlePanel.DesiredSize.Width - TitlePanel.Margin.Left - TitlePanel.Margin.Right <= room;
        TitlePanel.IsVisible = fits;
        var info = Core.Localization.L.T("Get Info");
        ToolTip.SetTip(InfoButton, fits ? info : NameText.Text + Environment.NewLine + info);
    }

    /// <summary>
    /// A photo or a video under the name row (owner, 0.6.1): the row lies over the picture, translucent and lightly
    /// blurred, and fades two seconds after the pointer leaves the window (with a video's bar). Songs keep it above.
    /// </summary>
    private void ApplyOverlay()
    {
        var overlay = Player.IsOverlay;
        Grid.SetRow(Player, overlay ? 0 : 1);
        Grid.SetRowSpan(Player, overlay ? 2 : 1);
        NameBlur.IsVisible = overlay;
        if (overlay)
        {
            NameBar[!Border.BackgroundProperty] = NameBar.GetResourceObservable(JGColor.OverlayScrim.ResourceKey,
                brush => brush is Avalonia.Media.ISolidColorBrush scrim
                    ? new Avalonia.Media.SolidColorBrush(scrim.Color, JGMetrics.QuickPreviewBarOpacity) : brush).ToBinding();
        }
        else
        {
            NameBar.ClearValue(Border.BackgroundProperty);
        }
        NameBar.Transitions = overlay && Settings.Motion.Enabled
            ? [new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromSeconds(JGMetrics.FadeDuration), Easing = new Avalonia.Animation.Easings.CubicEaseOut() }]
            : null;
        NameBar.Opacity = 1;
        NameBar.IsHitTestVisible = true;
    }

    private Avalonia.Threading.DispatcherTimer? _nameHide;

    /// <summary>A photo has no bar of its own: the name row fades by itself once the pointer left.</summary>
    private void StartPhotoHide()
    {
        if (!Player.IsOverlay || Player.Controls.IsVisible)
        {
            return;
        }
        _nameHide ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromSeconds(JGMetrics.QuickPreviewBarHideDelay),
            Avalonia.Threading.DispatcherPriority.Normal, (_, _) =>
            {
                _nameHide!.Stop();
                if (Player.IsOverlay)
                {
                    NameBar.Opacity = 0;
                    NameBar.IsHitTestVisible = false;
                }
            });
        _nameHide.Stop();
        _nameHide.Start();
    }

    private void ShowNameBar()
    {
        _nameHide?.Stop();
        if (NameBar.Opacity < 1)
        {
            NameBar.Opacity = 1;
            NameBar.IsHitTestVisible = true;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            var fullScreen = IsFullScreen;
            Player.IsFullScreen = fullScreen;
            NameRow.IsVisible = !fullScreen;
            Root.Classes.Set("fullScreen", fullScreen);
        }
    }

    private void Update()
    {
        if (_preview.Asset is not { } asset)
        {
            return;
        }
        NameText.Text = asset.Name;
        FitTitle();
        Star.AssetId = asset.Id;
        if (_info is not null && !ReferenceEquals(asset, _infoFor))
        {
            _infoFor = asset;
            _info.Fill([asset]);
        }
        // From song to song the blurred cover stays until the next one is known; a photo or a video clears it (MAC-028).
        var cover = asset.Kind == MediaKind.Audio ? _preview.Cover : null;
        var settled = asset.Kind != MediaKind.Audio || _preview.IsCoverKnown;
        if (settled && !ReferenceEquals(cover, _backdropFor))
        {
            _backdropFor = cover;
            (BackdropImage.Source as IDisposable)?.Dispose();
            BackdropImage.Source = cover?.CreateScaledBitmap(new PixelSize(BackdropPixels, BackdropPixels), Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
            Backdrop.IsVisible = cover is not null && Settings.SettingsStore.Current.Settings.CoverBackdrop;
        }
    }

    /// <summary>Get Info of the file shown, over this window.</summary>
    private void ShowInfo()
    {
        if (_preview?.Asset is not { } asset)
        {
            return;
        }
        _infoFor = asset;
        if (_info is null)
        {
            _info = new InfoWindow();
            _info.Closed += (_, _) => _info = null;
            _info.Fill([asset]);
            _info.Show(this);
        }
        else
        {
            _info.Fill([asset]);
            _info.Activate();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Settings.SettingsStore.Current.Shortcuts.Shortcut(Core.Actions.ShortcutCommand.GetInfo) is { } info && Matches(info.Gesture, e))
        {
            ShowInfo();
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }
        switch (e.Key)
        {
            case Key.Space or Key.Escape:
                if (IsFullScreen)
                {
                    WindowState = WindowState.Normal;
                }
                else
                {
                    Dismiss();
                }
                break;
            case Key.Enter:
                _preview.TogglePlay();
                break;
            case Key.F11:
                ToggleFullScreen();
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down:
                ArrowPressed?.Invoke(this, e.Key);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private static bool Matches(string gesture, KeyEventArgs e)
    {
        try
        {
            return KeyGesture.Parse(gesture).Matches(e);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
