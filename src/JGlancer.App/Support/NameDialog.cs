using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using JGlancer.Core.FileOps;
using JGlancer.Core.Localization;
using JGlancer.Design;

namespace JGlancer.App.Support;

/// <summary>
/// Asks for a name (a new folder): a heading, the field with the suggestion selected, Cancel and the action. Names
/// Windows refuses keep the action disabled.
/// </summary>
internal sealed class NameDialog : Window
{
    private NameDialog(string title, string suggestion, string action)
    {
        Title = title;
        Width = JGMetrics.DialogWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = this.GetResourceObservable(JGColor.BgPanel.ResourceKey).ToBinding();
        var field = new TextBox { Text = suggestion };
        Avalonia.Automation.AutomationProperties.SetName(field, L.T("Name"));
        var cancel = new Button { Content = L.T("Cancel"), IsCancel = true, Classes = { "secondary" } };
        var confirm = new Button { Content = action, IsDefault = true, Classes = { "primary" } };
        cancel.Click += (_, _) => Close(null);
        confirm.Click += (_, _) => Close(field.Text?.Trim());
        field.TextChanged += (_, _) => confirm.IsEnabled = FileNames.IsValid(field.Text?.Trim() ?? "");
        var heading = new TextBlock { Text = title, Classes = { "heading" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        heading[!TextBlock.ForegroundProperty] = this.GetResourceObservable(JGColor.TextPrimary.ResourceKey).ToBinding();
        Content = new StackPanel
        {
            Margin = new Thickness(JGSpacing.L),
            Spacing = JGSpacing.S,
            Children =
            {
                heading,
                field,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = JGSpacing.Xs, Margin = new Thickness(0, JGSpacing.S, 0, 0), Children = { cancel, confirm } },
            },
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
            }
        };
        Opened += (_, _) =>
        {
            field.Focus();
            field.SelectAll();
        };
    }

    /// <summary>The name entered, or null when cancelled.</summary>
    public static Task<string?> AskAsync(Window owner, string title, string suggestion, string action) =>
        new NameDialog(title, suggestion, action).ShowDialog<string?>(owner);
}
