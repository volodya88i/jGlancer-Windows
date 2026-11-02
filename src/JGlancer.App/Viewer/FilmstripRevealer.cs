using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using JGlancer.App.Settings;
using JGlancer.Core.Localization;
using JGlancer.Core.Settings;
using JGlancer.Design;

namespace JGlancer.App.Viewer;

/// <summary>
/// How a viewer's filmstrip comes and goes (macOS FilmstripPanel, Settings → Appearance → Filmstrip). Button: a
/// chevron above the strip hides and shows it, remembered across launches. Automatic: the strip appears while the
/// pointer is in the band above the bottom controls and hides <see cref="JGMetrics.FilmstripHideDelay"/> after it
/// leaves. Nothing for a folder of one file. The viewer passes pointer moves and asks <see cref="KeepsChrome"/>
/// before hiding its HUD.
/// </summary>
internal sealed class FilmstripRevealer
{
    private readonly Button _toggle;
    private readonly JGIcon _glyph;
    private readonly Control _panel;
    private readonly Filmstrip _strip;
    private readonly DispatcherTimer _hideTimer;
    private bool _isAvailable;
    private bool _isShown;
    private bool _isPinned;

    public FilmstripRevealer(Button toggle, JGIcon glyph, Control panel, Filmstrip strip)
    {
        _toggle = toggle;
        _glyph = glyph;
        _panel = panel;
        _strip = strip;
        _hideTimer = new DispatcherTimer(TimeSpan.FromSeconds(JGMetrics.FilmstripHideDelay), DispatcherPriority.Normal, (_, _) => Hide());
        _hideTimer.Stop();
        _toggle.Click += (_, _) => SettingsStore.Current.Update(s => s with { FilmstripCollapsed = !s.FilmstripCollapsed });
    }

    private static AppSettings Settings => SettingsStore.Current.Settings;

    private static bool IsAutomatic => Settings.FilmstripReveal == FilmstripReveal.Automatic;

    /// <summary>The HUD stays while the strip is on screen by itself (Automatic) or under the pointer (Button).</summary>
    public bool KeepsChrome => IsAutomatic ? _isShown : _panel.IsVisible && _panel.IsPointerOver;

    /// <summary>Starts following the settings; call when the viewer is attached.</summary>
    public void Attach()
    {
        SettingsStore.Current.Changed += OnSettingsChanged;
        Apply();
    }

    public void Detach()
    {
        SettingsStore.Current.Changed -= OnSettingsChanged;
        _hideTimer.Stop();
    }

    /// <summary>The folder has more than one file.</summary>
    public void SetAvailable(bool available)
    {
        // Every file change reports this: only a change of availability touches the strip (a click on it must not
        // hide it while the pointer is still there).
        if (available == _isAvailable)
        {
            return;
        }
        _isAvailable = available;
        Apply();
    }

    /// <summary>The pointer moved: <paramref name="inBand"/> when it is near the strip's place (Automatic only).</summary>
    public void Pointer(bool inBand)
    {
        if (!IsAutomatic)
        {
            return;
        }
        if (inBand && _isAvailable)
        {
            Show();
        }
        else
        {
            ScheduleHide();
        }
    }

    public void PointerLeft()
    {
        if (IsAutomatic)
        {
            ScheduleHide();
        }
    }

    /// <summary>Shows the strip and keeps it (developer snapshots).</summary>
    public void Pin()
    {
        _isPinned = true;
        if (IsAutomatic)
        {
            Show();
        }
    }

    private void OnSettingsChanged(object? sender, (AppSettings Old, AppSettings New) change)
    {
        if (change.Old.FilmstripReveal != change.New.FilmstripReveal || change.Old.FilmstripCollapsed != change.New.FilmstripCollapsed)
        {
            Apply();
        }
    }

    private void Apply()
    {
        _hideTimer.Stop();
        if (IsAutomatic)
        {
            _toggle.IsVisible = false;
            _panel.IsVisible = _isAvailable;
            SetShown(_isAvailable && (_isShown || _isPinned));
            return;
        }
        var collapsed = Settings.FilmstripCollapsed;
        _toggle.IsVisible = _isAvailable;
        _glyph.Data = collapsed ? JGIconData.ChevronUp : JGIconData.ChevronDown;
        var label = L.T(collapsed ? "Show Filmstrip" : "Hide Filmstrip");
        ToolTip.SetTip(_toggle, label);
        AutomationProperties.SetName(_toggle, label);
        _panel.IsVisible = _isAvailable && !collapsed;
        SetShown(_panel.IsVisible);
    }

    private void Show()
    {
        _hideTimer.Stop();
        SetShown(true);
    }

    private void ScheduleHide()
    {
        if (_isShown && !_isPinned && !_hideTimer.IsEnabled)
        {
            _hideTimer.Start();
        }
    }

    private void Hide()
    {
        _hideTimer.Stop();
        SetShown(false);
    }

    private void SetShown(bool shown)
    {
        _isShown = shown;
        _panel.Opacity = shown ? 1 : 0;
        _panel.IsHitTestVisible = shown;
        _strip.IsActive = shown;
    }
}
