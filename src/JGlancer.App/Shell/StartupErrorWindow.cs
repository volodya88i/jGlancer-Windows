using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using JGlancer.Core.Localization;
using JGlancer.Design;

namespace JGlancer.App.Shell;

/// <summary>
/// The library database could not be opened, but SQLite did not call it damaged (no permission, a full or read-only
/// disk, a lock held by another process, an I/O error; macOS MAC-031): the file stays where it is, and this window says
/// so, with Show in Explorer and Quit. The app ends when it closes.
/// </summary>
internal sealed class StartupErrorWindow : Window
{
    public StartupErrorWindow(string libraryPath)
    {
        Title = "jGlancer";
        Width = JGMetrics.DialogWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://jGlancer/Assets/jGlancer.ico")));
        this[!BackgroundProperty] = this.GetResourceObservable(JGColor.BgPanel.ResourceKey).ToBinding();
        JGWindowChrome.Apply(this);
        var heading = new JGAccentText
        {
            Markup = L.T("The library **can't open**."),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Classes = { "heading" },
        };
        heading[!TextBlock.ForegroundProperty] = this.GetResourceObservable(JGColor.TextPrimary.ResourceKey).ToBinding();
        var message = new TextBlock
        {
            Text = L.T("Check that the disk with the library folder is connected and can be written to, then open the app again."),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Classes = { "body" },
        };
        message[!TextBlock.ForegroundProperty] = this.GetResourceObservable(JGColor.TextSecondary.ResourceKey).ToBinding();
        var reveal = new Button { Content = L.T("Show in Explorer") };
        reveal.Click += (_, _) => Platform.Windows.ShellFiles.ShowInExplorer([libraryPath]);
        var quit = new Button { Content = L.T("Quit"), IsDefault = true, IsCancel = true, Classes = { "primary" } };
        quit.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(JGSpacing.L),
            Spacing = JGSpacing.S,
            Children =
            {
                heading,
                message,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = JGSpacing.Xs,
                    Margin = new Thickness(0, JGSpacing.S, 0, 0),
                    Children = { reveal, quit },
                },
            },
        };
    }
}
