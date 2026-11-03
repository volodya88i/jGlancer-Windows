using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using JGlancer.Design;

namespace JGlancer.App.Support;

/// <summary>
/// Frosted glass for panels inside a window (W-111): toasts and progress panels over the library. What the window drew
/// behind them is blurred (<see cref="BackdropBlur"/>) under a tint of their own color, so they read as one material with
/// the frosted menus. Solid when glass is off or a contrast theme shows.
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
        panel[!Border.BackgroundProperty] = panel.GetResourceObservable(color.ResourceKey,
            value => value is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, JGMetrics.WindowGlassTintOpacity) : value).ToBinding();
    }
}
