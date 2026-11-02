using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using JGlancer.Core.Localization;
using JGlancer.Design;

namespace JGlancer.App.Shell;

/// <summary>
/// Help ▸ About jGlancer (W12): the name in display type, the version, what the app is, its licence, links to the author's
/// GitHub and the source code (W-101), and the third-party notices (the licences of the bundled libraries and fonts,
/// shipped in the Licenses folder). Esc closes it.
/// </summary>
internal sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = L.T("About jGlancer");
        Width = JGMetrics.DialogWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://jGlancer/Assets/jGlancer.ico")));
        this[!BackgroundProperty] = this.GetResourceObservable(JGColor.BgPanel.ResourceKey).ToBinding();
        var name = Text("jGlancer", "display", JGColor.TextPrimary);
        var version = Text(L.T("Version {0}", Version), "mono", JGColor.TextSecondary);
        var what = Text(L.T("A fast, light media browser for photos, video and music."), "body", JGColor.TextSecondary);
        var rights = Text(Copyright, "body", JGColor.TextTertiary);
        var license = Text(L.T("Free software under the GNU General Public License 3.0."), "body", JGColor.TextTertiary);
        var github = Link(L.T("GitHub"), Core.Updates.AppUpdates.ProfileUrl);
        var source = Link(L.T("Source Code"), Core.Updates.AppUpdates.RepositoryUrl);
        var notices = new Button { Content = L.T("Third-Party Notices") };
        notices.Click += (_, _) => OpenNotices();
        var close = new Button { Content = L.T("Close"), IsDefault = true, IsCancel = true, Classes = { "primary" } };
        close.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(JGSpacing.L),
            Spacing = JGSpacing.S,
            Children =
            {
                name,
                version,
                what,
                rights,
                license,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = JGSpacing.Xs,
                    Children = { github, source },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = JGSpacing.Xs,
                    Margin = new Thickness(0, JGSpacing.S, 0, 0),
                    Children = { notices, close },
                },
            },
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    /// <summary>The app's version ("0.2.0"), without the build's commit suffix.</summary>
    public static string Version
    {
        get
        {
            var informational = typeof(AboutWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return informational?.Split('+')[0] ?? typeof(AboutWindow).Assembly.GetName().Version?.ToString(3) ?? "";
        }
    }

    private static string Copyright =>
        typeof(AboutWindow).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

    private TextBlock Text(string text, string style, JGColorToken color)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Classes = { style } };
        block[!TextBlock.ForegroundProperty] = this.GetResourceObservable(color.ResourceKey).ToBinding();
        return block;
    }

    /// <summary>A button that opens one of the app's own fixed addresses in the browser.</summary>
    private static Button Link(string title, string url)
    {
        var link = new Button { Content = title };
        ToolTip.SetTip(link, url);
        Avalonia.Automation.AutomationProperties.SetName(link, title);
        link.Click += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        return link;
    }

    /// <summary>Opens the notices in the program Windows uses for text files.</summary>
    private static void OpenNotices()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Licenses", "THIRD-PARTY-NOTICES.txt");
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
    }
}
