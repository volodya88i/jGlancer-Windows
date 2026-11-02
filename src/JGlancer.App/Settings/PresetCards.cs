using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using JGlancer.Core.Localization;
using JGlancer.Core.Settings;
using JGlancer.Design;

namespace JGlancer.App.Settings;

/// <summary>
/// The two looks as cards, one per row: one word and one short line (macOS PresetCards, DESIGN.md §8); the word in
/// display type on the first-launch screen. Rows, not columns, so long Russian words never shrink or clip.
/// </summary>
internal static class PresetCards
{
    public static StackPanel Build(AppearancePreset? selected, bool isLarge, Action<AppearancePreset> choose)
    {
        var cards = new StackPanel { Spacing = JGSpacing.S };
        foreach (var preset in Enum.GetValues<AppearancePreset>())
        {
            var name = SettingsUi.Text(Name(preset), isLarge ? "display" : "heading");
            name.MinWidth = isLarge ? JGMetrics.PresetNameWidthLarge : JGMetrics.PresetNameWidth;
            var line = SettingsUi.Text(Line(preset), "body", JGColor.TextSecondary, wraps: true);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = JGSpacing.L };
            Grid.SetColumn(line, 1);
            row.Children.Add(name);
            row.Children.Add(line);
            var card = new Button
            {
                Content = row,
                Classes = { "card" },
                Padding = isLarge ? new Thickness(JGSpacing.L, JGSpacing.M) : new Thickness(JGSpacing.M, JGSpacing.S),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            card.Classes.Set("on", preset == selected);
            AutomationProperties.SetName(card, Name(preset) + ". " + Line(preset));
            card.Click += (_, _) => choose(preset);
            cards.Children.Add(card);
        }
        return cards;
    }

    public static string Name(AppearancePreset preset) => preset == AppearancePreset.Beauty ? L.T("Beauty") : L.T("Performance");

    public static string Line(AppearancePreset preset) =>
        preset == AppearancePreset.Beauty ? L.T("Blur and smooth motion.") : L.T("Fewer effects, faster.");
}
