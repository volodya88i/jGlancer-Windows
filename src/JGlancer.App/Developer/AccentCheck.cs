using Avalonia.Controls;
using Avalonia.Media;
using JGlancer.App.Settings;
using JGlancer.Core.Settings;
using JGlancer.Design;

namespace JGlancer.App.Developer;

/// <summary>
/// <c>--accent-check &lt;log&gt;</c> (W-93): changes the accent while the app runs and logs what a bound brush and a
/// self-drawn control's lookup see after each change, then the default again; quits.
/// </summary>
internal static class AccentCheck
{
    public static void Attach(Window window, string log) => window.Opened += async (_, _) =>
    {
        var bound = new Border();
        bound.Bind(Border.BackgroundProperty, window.GetResourceObservable(JGColor.Accent.ResourceKey));
        var lines = new List<string>();
        var store = SettingsStore.Current;
        foreach (var color in new[] { "#FF2E93", "#FFFF00", "#101010", null })
        {
            store.Update(s => s with { AccentColor = color });
            await Task.Delay(TimeSpan.FromSeconds(0.2));
            lines.Add($"{color ?? "default"}: bound={(bound.Background as ISolidColorBrush)?.Color} lookup={(window.Content is Control content ? (content.Brush(JGColor.Accent) as ISolidColorBrush)?.Color : null)} dim={(window.Brush(JGColor.AccentDim) as ISolidColorBrush)?.Color}");
        }
        await File.WriteAllLinesAsync(log, lines);
        window.Close();
    };
}
