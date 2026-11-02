using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using JGlancer.Core.Localization;
using JGlancer.Core.Settings;
using JGlancer.Design;

namespace JGlancer.App.Settings;

/// <summary>
/// First launch (macOS OnboardingView, FEATURES §12): the two looks as large cards, one word and one line each, in
/// place of the library. Skip (or Esc) keeps the defaults, Performance. Shown once; Settings → Appearance changes the
/// look any time.
/// </summary>
internal sealed class OnboardingView : UserControl
{
    public OnboardingView()
    {
        var skip = new Button { Content = L.T("Skip"), Classes = { "tertiary" } };
        skip.Click += (_, _) => Choose(AppearancePreset.Performance);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(skip, 1);
        footer.Children.Add(SettingsUi.Text(L.T("You can change it any time."), "body", JGColor.TextTertiary, wraps: true));
        footer.Children.Add(skip);

        var content = new StackPanel
        {
            Spacing = JGSpacing.L,
            MaxWidth = JGMetrics.ReadableTextWidth * 2,
            Margin = new Avalonia.Thickness(JGSpacing.Xxl),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(new JGSectionHeader { Label = L.T("Choose a Look") });
        content.Children.Add(PresetCards.Build(null, isLarge: true, Choose));
        content.Children.Add(footer);
        Content = content;
        this.Bind(BackgroundProperty, this.GetResourceObservable(JGColor.BgBase.ResourceKey));
        Focusable = true;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                Choose(AppearancePreset.Performance);
            }
        };
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Input);
    }

    private static void Choose(AppearancePreset preset) =>
        SettingsStore.Current.Update(settings => preset.Applied(settings) with { HasChosenAppearance = true });
}
