using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using JGlancer.Core.Localization;
using JGlancer.Design;

namespace JGlancer.App.Support;

/// <summary>
/// A small modal question with several answers (macOS NSAlert with buttons): a heading, one line, then the answers
/// from the right, the first one default (Enter), and Cancel (Esc) answering nothing.
/// </summary>
internal sealed class ChoiceDialog<T> : Window
    where T : struct
{
    private ChoiceDialog(string title, string message, IReadOnlyList<(string Label, T Value)> choices)
    {
        Title = title;
        Width = JGMetrics.DialogWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = this.GetResourceObservable(JGColor.BgPanel.ResourceKey).ToBinding();
        JGWindowChrome.Apply(this);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = JGSpacing.Xs, Margin = new Thickness(0, JGSpacing.S, 0, 0) };
        var cancel = new Button { Content = L.T("Cancel"), IsCancel = true };
        cancel.Click += (_, _) => Close(null);
        buttons.Children.Add(cancel);
        for (var i = choices.Count - 1; i >= 0; i--)
        {
            var (label, value) = choices[i];
            var button = new Button { Content = label, IsDefault = i == 0 };
            if (i == 0)
            {
                button.Classes.Add("primary");
            }
            button.Click += (_, _) => Close(value);
            buttons.Children.Add(button);
        }
        var heading = new TextBlock { Text = title, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "heading" } };
        heading[!TextBlock.ForegroundProperty] = this.GetResourceObservable(JGColor.TextPrimary.ResourceKey).ToBinding();
        var line = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "body" } };
        line[!TextBlock.ForegroundProperty] = this.GetResourceObservable(JGColor.TextSecondary.ResourceKey).ToBinding();
        Content = new StackPanel { Margin = new Thickness(JGSpacing.L), Spacing = JGSpacing.S, Children = { heading, line, buttons } };
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is not Button)
            {
                BeginMoveDrag(e);
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
            }
        };
    }

    /// <summary>The chosen answer, or null for Cancel.</summary>
    public static Task<T?> ShowAsync(Window owner, string title, string message, IReadOnlyList<(string Label, T Value)> choices) =>
        new ChoiceDialog<T>(title, message, choices).ShowDialog<T?>(owner);
}
