using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using JGlancer.Core.Localization;
using JGlancer.Core.Settings;
using JGlancer.Design;
using JGlancer.Platform.Windows;

namespace JGlancer.App.Settings;

/// <summary>Settings → Appearance → Accent Color (W-93): ready colors, a hex field and Windows' color dialog.</summary>
internal sealed partial class SettingsWindow
{
    private uint ChosenAccent => AccentColorText.TryParse(S.AccentColor, out var chosen) ? chosen : JGAccent.DefaultColor;

    /// <summary>The default color is saved as "none", so a later change of the app's own accent reaches it.</summary>
    private void SetAccent(uint color) =>
        Set(s => s with { AccentColor = color == JGAccent.DefaultColor ? null : AccentColorText.Format(color) });

    private StackPanel AccentColorGroup()
    {
        var current = ChosenAccent;
        var swatches = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var preset in JGAccent.Presets)
        {
            swatches.Children.Add(Swatch(preset, preset.Color == current));
        }
        var other = new Button { Content = L.T("Other Color…"), VerticalAlignment = VerticalAlignment.Center };
        other.Click += (_, _) =>
        {
            if (ColorDialog.Choose(TryGetPlatformHandle()?.Handle ?? 0, ChosenAccent) is { } color)
            {
                SetAccent(color);
            }
        };
        return SettingsUi.Group(L.T("Accent Color"),
            swatches,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = JGSpacing.S, Children = { HexField(current), other } });
    }

    private Button Swatch(JGAccentPreset preset, bool isChosen)
    {
        var dot = new Border
        {
            Width = JGAccent.SwatchSize,
            Height = JGAccent.SwatchSize,
            CornerRadius = new CornerRadius(JGAccent.SwatchSize),
            Background = new ImmutableSolidColorBrush(Color.FromUInt32(preset.Color)),
        };
        var ring = new Border
        {
            Child = dot,
            Padding = new Thickness(JGSpacing.Xxs / 2),
            BorderThickness = new Thickness(JGMetrics.SelectionEdgeWidth),
            CornerRadius = new CornerRadius(JGAccent.SwatchSize),
            BorderBrush = Brushes.Transparent,
        };
        if (isChosen)
        {
            ring.Bind(Border.BorderBrushProperty, ring.GetResourceObservable(JGColor.TextPrimary.ResourceKey));
        }
        var name = L.T(preset.Name);
        var button = new Button { Content = ring, Classes = { "icon" }, Padding = new Thickness(JGSpacing.Xxs / 2) };
        ToolTip.SetTip(button, name);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => SetAccent(preset.Color);
        return button;
    }

    /// <summary>Takes the color on Enter or on leaving the field; anything else than a color goes back to the current one.</summary>
    private TextBox HexField(uint current)
    {
        var field = new TextBox { Text = AccentColorText.Format(current), MinWidth = JGMetrics.SettingsSidebarWidth / 2, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(field, L.T("Accent Color"));
        void Commit()
        {
            if (!AccentColorText.TryParse(field.Text, out var typed))
            {
                field.Text = AccentColorText.Format(ChosenAccent);
            }
            else if (typed != ChosenAccent)
            {
                SetAccent(typed);
            }
        }
        field.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };
        field.LostFocus += (_, _) => Commit();
        return field;
    }
}
