using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using JGlancer.Design;

namespace JGlancer.App.Settings;

/// <summary>
/// The pieces Settings is built from (macOS SettingsGroup, SettingsToggle, segmented pickers): a labelled group, a
/// switch in the app's accent, a segmented choice, text in the design's styles.
/// </summary>
internal static class SettingsUi
{
    public static TextBlock Text(string text, string style = "body", JGColorToken? color = null, bool wraps = false)
    {
        var block = new TextBlock
        {
            Text = text,
            Classes = { style },
            TextWrapping = wraps ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wraps ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable((color ?? JGColor.TextPrimary).ResourceKey));
        return block;
    }

    /// <summary>A section header in capitals, then the group's controls.</summary>
    public static StackPanel Group(string title, params Control[] children)
    {
        var group = new StackPanel { Spacing = JGSpacing.Xs };
        group.Children.Add(new JGSectionHeader { Label = title });
        group.Children.AddRange(children);
        return group;
    }

    /// <summary>A switch with its label on the left.</summary>
    public static Control Toggle(string title, bool isOn, Action<bool> set)
    {
        var toggle = new ToggleSwitch { IsChecked = isOn, OnContent = null, OffContent = null, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, title);
        toggle.IsCheckedChanged += (_, _) => set(toggle.IsChecked == true);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = JGMetrics.ControlHeight };
        var label = Text(title, wraps: true);
        row.Children.Add(label);
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);
        return row;
    }

    /// <summary>A setting's name on the left and its control on the right, as the toggles' rows.</summary>
    public static Control Labeled(string title, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = JGMetrics.ControlHeight };
        row.Children.Add(Text(title, wraps: true));
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    /// <summary>A segmented choice (macOS .segmented picker): the options side by side in one track.</summary>
    public static Control Segmented<T>(string name, IReadOnlyList<(string Label, T Value)> options, T current, Action<T> set)
    {
        var track = new StackPanel { Orientation = Orientation.Horizontal, Spacing = JGSpacing.Xxs };
        var buttons = new List<(Button Button, T Value)>();
        foreach (var (label, value) in options)
        {
            var button = new Button { Content = label, Classes = { "segment" } };
            button.Classes.Set("on", EqualityComparer<T>.Default.Equals(value, current));
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) =>
            {
                foreach (var (other, otherValue) in buttons)
                {
                    other.Classes.Set("on", EqualityComparer<T>.Default.Equals(otherValue, value));
                }
                set(value);
            };
            buttons.Add((button, value));
            track.Children.Add(button);
        }
        var border = new Border
        {
            Child = track,
            Padding = new Thickness(JGSpacing.Xxs / 2),
            CornerRadius = new CornerRadius(JGMetrics.RadiusMedium),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable(JGColor.BgPanel.ResourceKey));
        AutomationProperties.SetName(border, name);
        return border;
    }

    /// <summary>A hairline between rows.</summary>
    public static Border Hairline()
    {
        var line = new Border { Height = JGMetrics.OutlineWidth };
        line.Bind(Border.BackgroundProperty, line.GetResourceObservable(JGColor.LineHair.ResourceKey));
        return line;
    }

    /// <summary>A card with a hairline edge (a kind in Edit With).</summary>
    public static Border Card(Control content)
    {
        var card = new Border
        {
            Child = content,
            Padding = new Thickness(JGSpacing.M),
            CornerRadius = new CornerRadius(JGMetrics.RadiusLarge),
            BorderThickness = new Thickness(JGMetrics.OutlineWidth),
        };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable(JGColor.BgPanel.ResourceKey));
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable(JGColor.LineHair.ResourceKey));
        return card;
    }

    public static Button IconButton(string icon, string label, Action click, JGColorToken? color = null)
    {
        var glyph = new JGIcon { Data = icon };
        if (color is { } tint)
        {
            glyph.Bind(JGIcon.ForegroundProperty, glyph.GetResourceObservable(tint.ResourceKey));
        }
        var button = new Button { Content = glyph, Classes = { "icon" }, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(button, label);
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => click();
        return button;
    }
}
