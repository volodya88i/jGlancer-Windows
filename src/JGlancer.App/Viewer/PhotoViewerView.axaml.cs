using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using JGlancer.Core.Localization;
using JGlancer.Design;
using JGlancer.Platform.Windows;

namespace JGlancer.App.Viewer;

/// <summary>
/// The photo viewer (macOS PhotoViewerView): ← → page through the folder wherever the focus is, Esc returns to the
/// library, Ctrl+0 / 9 / 1 / = / - zoom. The HUD fades after <see cref="JGMetrics.HudIdleSeconds"/> without pointer
/// movement (and the cursor hides until it moves); the filmstrip comes and goes as <see cref="FilmstripRevealer"/> says.
/// Each wait is one single-shot timer restarted by activity, never a polling loop.
/// </summary>
internal sealed partial class PhotoViewerView : UserControl
{
    private readonly SwipePager _pager = new();
    private readonly DispatcherTimer _hudTimer;
    private readonly FilmstripRevealer _filmstrip;
    private PhotoViewerModel? _model;
    private bool _isHudVisible = true;
    private Point? _lastPointer;

    public PhotoViewerView()
    {
        InitializeComponent();
        FailedText.Text = L.T("Can't open this photo.");
        _hudTimer = new DispatcherTimer(TimeSpan.FromSeconds(JGMetrics.HudIdleSeconds), DispatcherPriority.Normal, (_, _) => HideHud());
        _hudTimer.Stop();
        _filmstrip = new FilmstripRevealer(FilmstripToggle, FilmstripToggleGlyph, FilmstripPanel, Strip);

        TopBar.BackRequested += (_, _) => LibraryRequested?.Invoke(this, EventArgs.Empty);
        RotateLeftButton.Click += (_, _) => _model?.Rotate(-1);
        RotateRightButton.Click += (_, _) => _model?.Rotate(1);
        FitButton.Click += (_, _) => Canvas.Zoom(ZoomCommand.Fit);
        ActualSizeButton.Click += (_, _) => Canvas.Zoom(ZoomCommand.ActualSize);
        PreviousButton.Click += (_, _) => Page(-1);
        NextButton.Click += (_, _) => Page(1);

        Canvas.Pager = _pager;
        Canvas.Neighbor = offset => _model?.NeighborPicture(offset);
        Canvas.Navigate += (_, direction) => Page(direction);
        Canvas.ZoomedBeyondFit += (_, _) => _model?.RequestFullResolution();
        _pager.Changed += (_, _) => Canvas.InvalidateVisual();
        _pager.Host = () => TopLevel.GetTopLevel(this);
        _pager.CanPage = direction => _model?.CanMove(direction) ?? false;
        _pager.Page = direction => _model?.Move(direction);

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, _) => ShowHud(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, (_, _) => ShowHud(), RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerExited += (_, _) => _filmstrip.PointerLeft();
    }

    /// <summary>Esc or "‹ Library".</summary>
    public event EventHandler? CloseRequested;

    /// <summary>"‹ Library" (it also loads the library when the app was opened for a file, D-65).</summary>
    public event EventHandler? LibraryRequested;

    public PhotoViewerModel? Model
    {
        get => _model;
        set
        {
            if (_model is not null)
            {
                _model.Changed -= OnModelChanged;
            }
            _model = value;
            Strip.Model = value;
            if (_model is not null)
            {
                _model.Changed += OnModelChanged;
            }
            Update();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Fades and the swipe follow Windows' animation effects setting (Reduce Motion counterpart).
        var animated = Settings.Motion.Enabled;
        // Settings → Appearance: the neighbor slides in while swiping (Beauty), or the photo turns at once.
        _pager.IsAnimated = animated && Settings.SettingsStore.Current.Settings.SwipeAnimation;
        if (animated)
        {
            Hud.Transitions = [Fade()];
            FilmstripPanel.Transitions = [Fade()];
        }
        _filmstrip.Attach();
        ShowHud();
        Dispatcher.UIThread.Post(() => Canvas.Focus(), DispatcherPriority.Input);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _hudTimer.Stop();
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

    private void OnModelChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        if (_model is not { } model)
        {
            Canvas.Display(null, null, 0);
            return;
        }
        Canvas.Display(model.CurrentId, model.Picture, model.QuarterTurns);
        var name = model.Current?.Name ?? "";
        TopBar.Show(name, model.Index, model.Count, model.CurrentId);
        Avalonia.Automation.AutomationProperties.SetName(Canvas, name);
        PreviousButton.IsVisible = model.CanMove(-1);
        NextButton.IsVisible = model.CanMove(1);
        FailedMessage.IsVisible = model.Failed;
        _filmstrip.SetAvailable(model.Count > 1);
    }

    private void Page(int offset)
    {
        _model?.Move(offset);
        ShowHud();
    }

    // MARK: Keyboard

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var control = e.KeyModifiers == KeyModifiers.Control;
        var plain = e.KeyModifiers == KeyModifiers.None;
        ZoomCommand? zoom = !control ? null : e.Key switch
        {
            Key.D0 or Key.NumPad0 => ZoomCommand.Fit,
            Key.D9 or Key.NumPad9 => ZoomCommand.Fill,
            Key.D1 or Key.NumPad1 => ZoomCommand.ActualSize,
            Key.OemPlus or Key.Add => ZoomCommand.ZoomIn,
            Key.OemMinus or Key.Subtract => ZoomCommand.ZoomOut,
            _ => null,
        };
        if (zoom is { } command)
        {
            Canvas.Zoom(command);
        }
        else if (plain && e.Key == Key.Left)
        {
            Page(-1);
        }
        else if (plain && e.Key == Key.Right)
        {
            Page(1);
        }
        else if (plain && e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            return;
        }
        e.Handled = true;
    }

    // MARK: HUD

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        // Layout changes can report the pointer again without movement; only real movement is activity.
        if (_lastPointer == point)
        {
            return;
        }
        _lastPointer = point;
        Cursor = null;
        ShowHud();
        _filmstrip.Pointer(point.Y >= Bounds.Height - JGMetrics.FilmstripHeight - JGMetrics.FilmstripRevealMargin);
    }

    /// <summary>Shows the HUD and restarts the idle wait.</summary>
    private void ShowHud()
    {
        if (!_isHudVisible)
        {
            _isHudVisible = true;
            Hud.Opacity = 1;
            Hud.IsHitTestVisible = true;
        }
        _hudTimer.Stop();
        _hudTimer.Start();
    }

    private void HideHud()
    {
        _hudTimer.Stop();
        // The filmstrip stays while the pointer is on it.
        if (_filmstrip.KeepsChrome)
        {
            return;
        }
        _isHudVisible = false;
        Hud.Opacity = 0;
        Hud.IsHitTestVisible = false;
        Cursor = new Cursor(StandardCursorType.None);
    }

    /// <summary>Shows the filmstrip and keeps it (developer snapshots).</summary>
    public void PinFilmstrip() => _filmstrip.Pin();
}
