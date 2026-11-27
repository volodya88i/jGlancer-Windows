using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using JGlancer.Design;

namespace JGlancer.App.Library;

/// <summary>
/// The folding sidebar (macOS D-128, MAC-050/051/053): docked beside the files, folded into a strip by hand (its fold
/// button, Ctrl+B, View ▸ Show Sidebar) or by itself in a window narrower than <see cref="JGMetrics.SidebarAutoCollapseWidth"/>,
/// or out over the files from the strip while the pointer rests on it. One panel throughout: its visible width follows
/// a spring (<see cref="JGMetrics.SidebarSpringResponse"/>), the names fade, the icons glide (SidebarView.Fold.cs).
/// Docking from the strip opens the panel first, then the files make room; folding gives the files the room at once,
/// under the panel closing over them.
/// </summary>
internal sealed partial class LibraryView
{
    private bool _docked = true;
    private bool _foldStarted;
    private bool _peekOpen;
    private bool _peekSuppressed;
    private bool _pointerInPanel;
    private DispatcherTimer? _peekOpenTimer;
    private DispatcherTimer? _peekCloseTimer;
    private double _dockedWidth = JGMetrics.SidebarWidth;

    // The spring: where the panel is (0 the strip, 1 open), its speed, where it goes, and when it set off.
    private double _openness = 1;
    private double _speed;
    private double _target = 1;
    private bool _animating;
    private readonly Stopwatch _foldClock = new();
    private TimeSpan? _lastFrame;

    /// <summary>The sidebar is docked beside the files (View ▸ Show Sidebar's check).</summary>
    public bool IsSidebarDocked => _docked;

    private bool IsNarrow => Bounds.Width > 0 && Bounds.Width < JGMetrics.SidebarAutoCollapseWidth;

    private bool WantsDocked => Model is { } model && !model.State.SidebarCollapsed && !IsNarrow;

    private void AttachSidebarFold()
    {
        Sidebar.FoldClicked += (_, _) =>
        {
            if (_docked)
            {
                FoldSidebar();
            }
            else
            {
                ShowSidebar();
            }
        };
        Sidebar.ShowClicked += (_, _) => ShowSidebar();
        Sidebar.MenuClosed += (_, _) => CloseIfLeft();
        SidebarPanel.PointerEntered += (_, e) => OnPanelPointer(e, inside: true);
        SidebarPanel.PointerMoved += (_, e) => OnPanelPointer(e, inside: true);
        SidebarPanel.PointerExited += (_, e) => OnPanelPointer(e, inside: false);
        // Files dragged onto the strip open it at once, to be dropped on a folder, an album or the Trash.
        SidebarPanel.AddHandler(DragDrop.DragEnterEvent, (_, _) =>
        {
            if (!_docked && !_peekOpen)
            {
                OpenPeek();
            }
        });
    }

    /// <summary>Ctrl+B and View ▸ Show Sidebar: folds the docked sidebar, docks a folded one.</summary>
    public void ToggleSidebar()
    {
        if (_docked)
        {
            FoldSidebar();
        }
        else
        {
            ShowSidebar();
        }
    }

    private void FoldSidebar()
    {
        if (_pointerInPanel)
        {
            // Folded under the pointer: it does not come out again until the pointer has left it.
            _peekSuppressed = true;
        }
        Model?.SetSidebarCollapsed(true);
    }

    /// <summary>Docks the sidebar; a window too narrow for it is widened first (kept on its screen; not past it).</summary>
    private void ShowSidebar()
    {
        Model?.SetSidebarCollapsed(false);
        if (IsNarrow && TopLevel.GetTopLevel(this) is Window { WindowState: WindowState.Normal } window
            && window.Screens.ScreenFromWindow(window) is { } screen)
        {
            var scaling = screen.Scaling;
            var extra = window.Bounds.Width - Bounds.Width;
            var width = JGMetrics.SidebarAutoCollapseWidth + extra;
            if (width * scaling <= screen.WorkingArea.Width)
            {
                window.Width = width;
                var right = window.Position.X + (int)Math.Ceiling(width * scaling);
                if (right > screen.WorkingArea.Right)
                {
                    window.Position = new PixelPoint(screen.WorkingArea.Right - (int)Math.Ceiling(width * scaling), window.Position.Y);
                }
            }
        }
        UpdateSidebarFold();
    }

    /// <summary>The view state or the window's width changed: docked or folded, and the panel's geometry.</summary>
    private void UpdateSidebarFold()
    {
        if (Model is null)
        {
            return;
        }
        var docked = WantsDocked;
        if (_foldStarted && docked == _docked)
        {
            UpdateSidebarGeometry();
            return;
        }
        var first = !_foldStarted;
        _foldStarted = true;
        _docked = docked;
        CancelPeekTimers();
        if (docked)
        {
            var wasOpen = _peekOpen;
            _peekOpen = false;
            Sidebar.SetCompact(false);
            Sidebar.FoldRole = SidebarFoldRole.Hide;
            // From the panel over the files the files only make room; from the strip the panel opens first.
            if (!wasOpen || first)
            {
                AnimateTo(1, instant: first);
            }
        }
        else
        {
            _peekOpen = false;
            Sidebar.SetCompact(true);
            AnimateTo(0, instant: first);
        }
        UpdateSidebarGeometry();
    }

    /// <summary>The panel's width, its room beside the files, its shadow and the frame of its content.</summary>
    private void UpdateSidebarGeometry()
    {
        var strip = JGMetrics.SidebarStripWidth;
        var width = Bounds.Width > 0 ? Bounds.Width : double.PositiveInfinity;
        var peekWidth = Math.Max(JGMetrics.SidebarMinWidth, Math.Min(Model?.State.SidebarWidth ?? JGMetrics.SidebarWidth, width - JGMetrics.LibraryPaneMinWidth));
        var content = _docked ? _dockedWidth : peekWidth;
        var visible = strip + (content - strip) * _openness;
        visible = Math.Clamp(visible, strip, content);
        // Docking makes room once the panel is open; the strip's room is taken at once when folding.
        var room = _docked && (!_animating || _openness >= 0.98) ? content : strip;
        Columns.ColumnDefinitions[0].Width = new GridLength(room);
        Sidebar.Width = content;
        SidebarPanel.Width = visible + JGMetrics.OutlineWidth;
        var over = visible > room + 0.5;
        SidebarShadow.BoxShadow = over
            ? new BoxShadows(new BoxShadow
            {
                Blur = JGMetrics.SidebarPeekShadowRadius,
                Color = Color.FromArgb((byte)Math.Round(255 * JGMetrics.SidebarPeekShadowOpacity), 0, 0, 0),
            })
            : default;
        SidebarHandle.IsVisible = _docked && room > strip;
        var (labels, stripOnly) = FoldFades();
        var flip = _target >= 0.5 ? _openness : 1 - _openness;
        Sidebar.ApplyMorph(_openness, labels, stripOnly, _animating ? flip : 1);
    }

    /// <summary>
    /// The opacity of the names and of what only the strip shows: opening, the names come in a third of the fade after
    /// the start (ease in) while the strip's own things go in half of it (ease out); folding, the other way round.
    /// </summary>
    private (double Labels, double Strip) FoldFades()
    {
        if (!_animating)
        {
            return _target >= 0.5 ? (1, 0) : (0, 1);
        }
        var fade = JGMetrics.SidebarLabelFade;
        var elapsed = _foldClock.Elapsed.TotalSeconds;
        var comingIn = Math.Clamp((elapsed - fade / 3) / fade, 0, 1);
        comingIn *= comingIn;
        var goingOut = Math.Clamp(elapsed / (fade / 2), 0, 1);
        goingOut = 1 - (1 - goingOut) * (1 - goingOut);
        return _target >= 0.5 ? (comingIn, 1 - goingOut) : (1 - goingOut, comingIn);
    }

    /// <summary>Sets the spring off toward <paramref name="target"/>; at once without motion (Settings) or at the start.</summary>
    private void AnimateTo(double target, bool instant = false)
    {
        _target = target;
        _foldClock.Restart();
        // Without a window yet there are no frames to animate with: the start's state applies at once.
        if (instant || !Settings.Motion.Enabled || TopLevel.GetTopLevel(this) is null)
        {
            _openness = target;
            _speed = 0;
            _animating = false;
            UpdateSidebarGeometry();
            return;
        }
        if (!_animating)
        {
            _animating = true;
            _lastFrame = null;
            TopLevel.GetTopLevel(this)!.RequestAnimationFrame(OnFoldFrame);
        }
    }

    private void OnFoldFrame(TimeSpan now)
    {
        if (!_animating)
        {
            return;
        }
        var step = _lastFrame is { } last ? Math.Min((now - last).TotalSeconds, 0.05) : 1.0 / 60;
        _lastFrame = now;
        // SwiftUI's spring(response:dampingFraction:): stiffness (2π / response)², damping 4π · fraction / response.
        var stiffness = Math.Pow(2 * Math.PI / JGMetrics.SidebarSpringResponse, 2);
        var damping = 4 * Math.PI * JGMetrics.SidebarSpringDamping / JGMetrics.SidebarSpringResponse;
        const double Substep = 1.0 / 480;
        for (var t = 0.0; t < step; t += Substep)
        {
            var dt = Math.Min(Substep, step - t);
            _speed += (-stiffness * (_openness - _target) - damping * _speed) * dt;
            _openness += _speed * dt;
        }
        if (Math.Abs(_openness - _target) < 0.001 && Math.Abs(_speed) < 0.01 && _foldClock.Elapsed.TotalSeconds > JGMetrics.SidebarLabelFade * 4 / 3)
        {
            _openness = _target;
            _speed = 0;
            _animating = false;
        }
        UpdateSidebarGeometry();
        if (_animating)
        {
            if (TopLevel.GetTopLevel(this) is { } top)
            {
                top.RequestAnimationFrame(OnFoldFrame);
            }
            else
            {
                // Taken out of its window mid-way: it ends where it was going.
                _openness = _target;
                _speed = 0;
                _animating = false;
                UpdateSidebarGeometry();
            }
        }
    }

    // MARK: Out over the files

    /// <summary>
    /// The pointer on the folded sidebar: resting under its first row (Show Sidebar) opens it after a moment, in the
    /// active window only; once open, all of it keeps it open; leaving it closes it a moment later.
    /// </summary>
    private void OnPanelPointer(PointerEventArgs e, bool inside)
    {
        _pointerInPanel = inside;
        if (_docked)
        {
            return;
        }
        if (!inside)
        {
            _peekOpenTimer?.Stop();
            _peekSuppressed = false;
            if (_peekOpen)
            {
                StartTimer(ref _peekCloseTimer, JGMetrics.SidebarPeekCloseDelay, CloseIfLeft);
            }
            return;
        }
        _peekCloseTimer?.Stop();
        var opensHere = _peekOpen || e.GetPosition(SidebarPanel).Y > JGMetrics.BarHeight;
        if (!opensHere)
        {
            _peekOpenTimer?.Stop();
            return;
        }
        if (!_peekOpen && !_peekSuppressed && _peekOpenTimer?.IsEnabled != true
            && TopLevel.GetTopLevel(this) is Window { IsActive: true })
        {
            StartTimer(ref _peekOpenTimer, JGMetrics.SidebarPeekOpenDelay, OpenPeek);
        }
    }

    private void OpenPeek()
    {
        _peekOpenTimer?.Stop();
        _peekCloseTimer?.Stop();
        if (_docked || _peekOpen)
        {
            return;
        }
        _peekOpen = true;
        Sidebar.SetCompact(false);
        Sidebar.FoldRole = SidebarFoldRole.KeepOpen;
        AnimateTo(1);
    }

    /// <summary>
    /// Closes the sidebar over the files once the pointer has left it, unless something started in it is still open: a
    /// row's menu, a dialog (the window is not active then) or an album's name being edited; it is asked again when that
    /// ends.
    /// </summary>
    private void CloseIfLeft()
    {
        _peekCloseTimer?.Stop();
        if (!_peekOpen || _pointerInPanel || Sidebar.IsHeld || TopLevel.GetTopLevel(this) is Window { IsActive: false })
        {
            return;
        }
        _peekOpen = false;
        Sidebar.SetCompact(true);
        AnimateTo(0);
    }

    private void CancelPeekTimers()
    {
        _peekOpenTimer?.Stop();
        _peekCloseTimer?.Stop();
    }

    private static void StartTimer(ref DispatcherTimer? timer, double seconds, Action tick)
    {
        timer?.Stop();
        var made = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        made.Tick += (_, _) =>
        {
            made.Stop();
            tick();
        };
        timer = made;
        made.Start();
    }

    /// <summary>Opens the folded sidebar over the files without the pointer (developer snapshots).</summary>
    public void PeekSidebar()
    {
        if (!_docked)
        {
            OpenPeek();
        }
    }
}

/// <summary>What the developer check of the fold reads (<c>--sidebar-check</c>).</summary>
internal sealed partial class LibraryView
{
    internal double SidebarVisibleWidth => SidebarPanel.Bounds.Width;

    internal double SidebarRoom => Columns.ColumnDefinitions[0].ActualWidth;

    internal bool IsSidebarAnimating => _animating;

    /// <summary>Whether a press at <paramref name="point"/> (in this view) would reach the sidebar rather than the files.</summary>
    internal bool HitsSidebar(Point point) =>
        this.InputHitTest(point) is Visual hit && Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<SidebarView>(hit, includeSelf: true) is not null;

    /// <summary>The pointer left the sidebar over the files.</summary>
    internal void LeaveSidebar()
    {
        _pointerInPanel = false;
        CloseIfLeft();
    }
}
