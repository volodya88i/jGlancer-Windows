using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using JGlancer.Design;

namespace JGlancer.App.Support;

/// <summary>
/// Frosted glass for panels inside a window (W-111): toasts and progress panels over the library, the library's top bar
/// and status bubble over the files. What the window drew behind them is blurred (<see cref="BackdropBlur"/>) under a
/// tint of their own color, so they read as one material with the frosted menus. Solid when glass is off or a contrast
/// theme shows.
/// </summary>
internal static class WindowGlass
{
    /// <summary>Whether glass is wanted now: Settings → Appearance → Frosted Glass, and no contrast theme.</summary>
    public static bool IsOn => Settings.SettingsStore.Current.Settings.FrostedGlass && !JGTheme.IsContrastTheme;

    /// <summary>Puts <paramref name="panel"/>'s content over a blurred backdrop and makes its background <paramref name="color"/> tinted.</summary>
    public static void Frost(Border panel, JGColorToken color)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(color);
        if (!IsOn)
        {
            return;
        }
        var content = panel.Child;
        panel.Child = null;
        panel.ClipToBounds = true;
        panel.Child = new Panel { Children = { new BackdropBlur { Radius = JGMetrics.WindowGlassBlur }, content! } };
        panel[!Border.BackgroundProperty] = Tinted(panel, color, JGMetrics.WindowGlassTintOpacity).ToBinding();
    }

    /// <summary>
    /// Frost for a long-lived panel (the library's bars): glass tinted <paramref name="tint"/> of <paramref name="color"/>
    /// while glass is on, the plain color otherwise, following the setting and the theme as they change.
    /// </summary>
    public static void FrostLive(Border panel, JGColorToken color, double tint)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(color);
        var content = panel.Child;
        panel.Child = null;
        panel.ClipToBounds = true;
        var blur = new BackdropBlur { Radius = JGMetrics.WindowGlassBlur };
        panel.Child = new Panel { Children = { blur, content! } };
        bool? applied = null;
        void Apply()
        {
            var on = IsOn;
            if (applied == on)
            {
                return;
            }
            applied = on;
            blur.IsVisible = on;
            panel[!Border.BackgroundProperty] = (on ? Tinted(panel, color, tint) : panel.GetResourceObservable(color.ResourceKey)).ToBinding();
        }
        void OnSettings(object? sender, (Core.Settings.AppSettings Old, Core.Settings.AppSettings New) change) => Apply();
        void OnTheme(object? sender, EventArgs e) => Apply();
        panel.AttachedToVisualTree += (_, _) =>
        {
            Settings.SettingsStore.Current.Changed += OnSettings;
            if (Application.Current is { } app)
            {
                app.ActualThemeVariantChanged += OnTheme;
            }
            Apply();
        };
        panel.DetachedFromVisualTree += (_, _) =>
        {
            Settings.SettingsStore.Current.Changed -= OnSettings;
            if (Application.Current is { } app)
            {
                app.ActualThemeVariantChanged -= OnTheme;
            }
        };
        Apply();
    }

    private static IObservable<object?> Tinted(Control panel, JGColorToken color, double opacity) =>
        panel.GetResourceObservable(color.ResourceKey,
            value => value is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, opacity) : value);
}
