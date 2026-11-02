using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using JGlancer.Core.Localization;
using JGlancer.Design;

namespace JGlancer.App.Support;

/// <summary>
/// A small modal question (macOS JGDialog / confirmation dialog): a heading, one line, Cancel and the action. Esc or
/// Cancel answers no, Enter or the action answers yes.
/// </summary>
internal sealed class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message, string action)
    {
        Title = title;
        Width = JGMetrics.DialogWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = this.GetResourceObservable(JGColor.BgPanel.ResourceKey).ToBinding();
        JGWindowChrome.Apply(this);
        var cancel = new Button { Content = L.T("Cancel"), IsCancel = true };
        var confirm = new Button { Content = action, IsDefault = true, Classes = { "primary" } };
        cancel.Click += (_, _) => Close(false);
        confirm.Click += (_, _) => Close(true);
        var heading = new TextBlock { Text = title, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "heading" } };
        heading[!TextBlock.ForegroundProperty] = this.GetResourceObservable(JGColor.TextPrimary.ResourceKey).ToBinding();
        var line = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "body" } };
        line[!TextBlock.ForegroundProperty] = this.GetResourceObservable(JGColor.TextSecondary.ResourceKey).ToBinding();
        Content = new StackPanel
        {
            Margin = new Thickness(JGSpacing.L),
            Spacing = JGSpacing.S,
            Children =
            {
                heading,
                line,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = JGSpacing.Xs,
                    Margin = new Thickness(0, JGSpacing.S, 0, 0),
                    Children = { cancel, confirm },
                },
            },
        };
        // The whole dialog drags like a title bar.
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
                Close(false);
            }
        };
    }

    public static async Task<bool> ShowAsync(Window owner, string title, string message, string action) =>
        await new ConfirmDialog(title, message, action).ShowDialog<bool?>(owner) == true;
}
