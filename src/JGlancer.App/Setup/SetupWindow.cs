using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using JGlancer.Core;
using JGlancer.Core.Localization;
using JGlancer.Design;
using JGlancer.Platform.Windows.Setup;

namespace JGlancer.App.Setup;

/// <summary>
/// The installer and uninstaller window in the app's own look (W-73): the name in display type, the choices (for
/// whom, where, a desktop shortcut), a progress bar, and the end (open jGlancer). An update keeps the place and the
/// choice of the version installed. Removing asks whether the library, the thumbnail cache and the settings go too
/// (off). Media files are never touched.
/// </summary>
internal sealed class SetupWindow : Window
{
    private readonly SetupMode _mode;
    private readonly ContentControl _page = new();
    private readonly InstalledApp? _installed = InstallLayout.FindInstalled();
    private InstallScope _scope = InstallScope.CurrentUser;
    private string _target = InstallLayout.For(InstallScope.CurrentUser).ProgramDirectory;
    private bool _desktop = true;
    private readonly HashSet<Core.Model.MediaKind> _defaults = [];

    public SetupWindow(SetupMode mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        _mode = mode;
        Title = mode.Uninstall ? L.T("Remove jGlancer") : L.T("jGlancer Setup");
        Width = JGMetrics.QuickPreviewWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://jGlancer/Assets/jGlancer.ico")));
        this[!BackgroundProperty] = this.GetResourceObservable(JGColor.BgPanel.ResourceKey).ToBinding();
        this[!ForegroundProperty] = this.GetResourceObservable(JGColor.TextPrimary.ResourceKey).ToBinding();
        FontFamily = JGFonts.Text;
        Content = new StackPanel
        {
            Margin = new Thickness(JGSpacing.Xl),
            Spacing = JGSpacing.L,
            Children = { Header(), _page },
        };
        if (_installed is { } installed)
        {
            // An update goes where the installed version is, for the same people.
            _scope = installed.Scope;
            _target = installed.Directory;
        }
        if (mode.Scope is { } scope)
        {
            _scope = scope;
            _target = mode.Target ?? InstallLayout.For(scope).ProgramDirectory;
            _desktop = mode.Desktop;
            _defaults.UnionWith(mode.Defaults);
        }
        if (mode.Uninstall)
        {
            ShowRemove();
        }
        else if (mode.Elevated)
        {
            Opened += (_, _) => _ = InstallAsync();
        }
        else
        {
            ShowChoices();
        }
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !_busy)
            {
                Close();
                e.Handled = true;
            }
        };
        Closing += (_, e) => e.Cancel = _busy;
    }

    private bool _busy;

    private static string Version => Shell.AboutWindow.Version;

    private StackPanel Header()
    {
        var name = Text(AppIdentity.Name, "display", JGColor.TextPrimary);
        var version = Text(L.T("Version {0}", Version), "mono", JGColor.TextSecondary);
        var what = Text(L.T("A fast, light media browser for photos, video and music."), "body", JGColor.TextSecondary);
        return new StackPanel { Spacing = JGSpacing.Xs, Children = { name, version, what } };
    }

    // MARK: Install

    private void ShowChoices()
    {
        var panel = new StackPanel { Spacing = JGSpacing.M };
        if (_installed is { } installed)
        {
            panel.Children.Add(Text(installed.Version == Version
                ? L.T("jGlancer {0} is installed here; it will be installed again.", Version)
                : L.T("jGlancer {0} is installed here; it will be updated to {1}.", installed.Version, Version), "body", JGColor.TextSecondary));
            panel.Children.Add(PathText(installed.Directory));
        }
        else
        {
            var forMe = Choice(L.T("Just me"), L.T("No administrator needed."), InstallScope.CurrentUser);
            var forAll = Choice(L.T("Everyone on this PC"), L.T("Installs in Program Files; Windows asks for an administrator."), InstallScope.AllUsers);
            panel.Children.Add(Label(L.T("Install for")));
            panel.Children.Add(new StackPanel { Spacing = JGSpacing.Xs, Children = { forMe, forAll } });
            panel.Children.Add(Label(L.T("Folder")));
            var path = PathText(_target);
            var change = new Button { Content = L.T("Change…") };
            change.Click += async (_, _) =>
            {
                if (await Support.FolderPicker.ChooseAsync(this) is [var chosen, ..])
                {
                    _target = Path.Combine(chosen, AppIdentity.Name);
                    path.Text = _target;
                }
            };
            void Choose(InstallScope scope)
            {
                _scope = scope;
                _target = InstallLayout.For(scope).ProgramDirectory;
                path.Text = _target;
                forMe.IsChecked = scope == InstallScope.CurrentUser;
                forAll.IsChecked = scope == InstallScope.AllUsers;
            }
            forMe.IsCheckedChanged += (_, _) =>
            {
                if (forMe.IsChecked == true)
                {
                    Choose(InstallScope.CurrentUser);
                }
            };
            forAll.IsCheckedChanged += (_, _) =>
            {
                if (forAll.IsChecked == true)
                {
                    Choose(InstallScope.AllUsers);
                }
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = JGSpacing.S };
            row.Children.Add(path);
            Grid.SetColumn(change, 1);
            row.Children.Add(change);
            panel.Children.Add(row);
        }
        var desktop = new CheckBox { Content = L.T("Shortcut on the desktop"), IsChecked = _desktop };
        desktop.IsCheckedChanged += (_, _) => _desktop = desktop.IsChecked == true;
        panel.Children.Add(desktop);
        // The default app (W-80): the kinds jGlancer does not open yet; Windows confirms after the install.
        var kinds = Settings.DefaultApps.Kinds.Where(kind => !Settings.DefaultApps.IsDefault(kind)).ToList();
        if (kinds.Count > 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = JGSpacing.M };
            foreach (var kind in kinds)
            {
                var box = new CheckBox { Content = Settings.DefaultApps.Title(kind), IsChecked = _defaults.Contains(kind) };
                box.IsCheckedChanged += (_, _) =>
                {
                    if (box.IsChecked == true)
                    {
                        _defaults.Add(kind);
                    }
                    else
                    {
                        _defaults.Remove(kind);
                    }
                };
                row.Children.Add(box);
            }
            panel.Children.Add(Label(L.T("Open my files with jGlancer")));
            panel.Children.Add(row);
        }
        var install = new Button { Content = _installed is null ? L.T("Install") : _installed.Version == Version ? L.T("Reinstall") : L.T("Update"), IsDefault = true, Classes = { "primary" } };
        install.Click += async (_, _) => await InstallAsync();
        panel.Children.Add(Buttons(Cancel(), install));
        _page.Content = panel;
    }

    private RadioButton Choice(string title, string detail, InstallScope scope)
    {
        var radio = new RadioButton { GroupName = "scope", IsChecked = _scope == scope };
        radio.Content = new StackPanel
        {
            Spacing = JGSpacing.Xxs,
            Children = { Text(title, "body", JGColor.TextPrimary), Text(detail, "body", JGColor.TextTertiary) },
        };
        return radio;
    }

    private async Task InstallAsync()
    {
        // Everyone on the PC: the same choices again, in a process Windows lets write there.
        if (_scope == InstallScope.AllUsers && !Environment.IsPrivilegedProcess)
        {
            var arguments = $"--setup --elevated --scope all --target \"{_target}\"" + (_desktop ? " --desktop" : "")
                + (_defaults.Count > 0 ? " --defaults " + string.Join(',', _defaults).ToLowerInvariant() : "");
            try
            {
                using var elevated = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, arguments) { UseShellExecute = true, Verb = "runas" });
                if (elevated is not null)
                {
                    Hide();
                    await elevated.WaitForExitAsync();
                    Close();
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The administrator prompt was declined: the choices stay.
            }
            return;
        }
        var layout = InstallLayout.For(_scope, _target);
        if (!await WaitForClosedAsync(layout.Executable))
        {
            return;
        }
        var bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = JGMetrics.OutlineWidth * 3, MinHeight = 0 };
        bar[!ProgressBar.ForegroundProperty] = this.GetResourceObservable(JGColor.Accent.ResourceKey).ToBinding();
        _page.Content = new StackPanel { Spacing = JGSpacing.M, Children = { Text(L.T("Installing…"), "heading", JGColor.TextPrimary), bar } };
        _busy = true;
        Platform.Windows.Setup.InstallTexts.Description = L.T("A fast, light media browser for photos, video and music.");
        var source = AppContext.BaseDirectory;
        var desktop = _desktop;
        try
        {
            await Task.Run(() => Installer.Install(source, layout, Version, desktop, share => Dispatcher.UIThread.Post(() => bar.Value = share)));
            ShellLink.NotifyIconsChanged();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.Runtime.InteropServices.COMException)
        {
            _busy = false;
            ShowProblem(L.T("jGlancer could not be installed."), error.Message);
            return;
        }
        _busy = false;
        if (_defaults.Count > 0)
        {
            // Windows' own page: one press of "Set default" makes it so.
            Settings.DefaultApps.Offer(layout.Executable, _defaults);
        }
        ShowInstalled(layout);
    }

    private void ShowInstalled(InstallLayout layout)
    {
        var open = new Button { Content = L.T("Open jGlancer"), IsDefault = true, Classes = { "primary" } };
        open.Click += (_, _) =>
        {
            // From an administrator's setup (for everyone) through Explorer, so the app runs as the user: an elevated
            // app could not take files dragged from Explorer.
            var start = Environment.IsPrivilegedProcess
                ? new ProcessStartInfo("explorer.exe") { ArgumentList = { layout.Executable } }
                : new ProcessStartInfo(layout.Executable) { UseShellExecute = true };
            Process.Start(start)?.Dispose();
            Close();
        };
        var close = new Button { Content = L.T("Close"), IsCancel = true };
        close.Click += (_, _) => Close();
        _page.Content = new StackPanel
        {
            Spacing = JGSpacing.M,
            Children =
            {
                Text(L.T("jGlancer is ready."), "heading", JGColor.TextPrimary),
                Text(L.T("Find it in the Start menu. Your files stay where they are: add a folder to begin."), "body", JGColor.TextSecondary),
                Text(_defaults.Count > 0 ? L.T("Windows asks you to confirm: on the page that opens, press “Set default”.") : "", "body", JGColor.TextTertiary),
                Buttons(close, open),
            },
        };
    }

    // MARK: Remove

    private void ShowRemove()
    {
        var data = new CheckBox { Content = L.T("Also delete my library, thumbnail cache and settings") };
        var remove = new Button { Content = L.T("Remove|uninstall"), IsDefault = true, Classes = { "primary" } };
        remove.Click += async (_, _) => await RemoveAsync(data.IsChecked == true);
        _page.Content = new StackPanel
        {
            Spacing = JGSpacing.M,
            Children =
            {
                Text(L.T("Remove jGlancer from this PC?"), "heading", JGColor.TextPrimary),
                Text(L.T("Your photos, videos and music stay where they are."), "body", JGColor.TextSecondary),
                data,
                Buttons(Cancel(), remove),
            },
        };
    }

    private async Task RemoveAsync(bool userData)
    {
        var installed = _installed;
        var directory = _mode.From ?? installed?.Directory;
        if (directory is null)
        {
            ShowProblem(L.T("jGlancer is not installed."), "");
            return;
        }
        var layout = InstallLayout.For(installed?.Scope ?? InstallScope.CurrentUser, directory);
        if (!await WaitForClosedAsync(layout.Executable))
        {
            return;
        }
        _busy = true;
        _page.Content = Text(L.T("Removing…"), "heading", JGColor.TextPrimary);
        string[] folders = userData ? [AppIdentity.DataDirectory, Path.GetDirectoryName(AppIdentity.CacheDirectory)!] : [];
        try
        {
            await Task.Run(() => Installer.Uninstall(layout, folders));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _busy = false;
            ShowProblem(L.T("jGlancer could not be removed completely."), error.Message);
            return;
        }
        _busy = false;
        var close = new Button { Content = L.T("Close"), IsDefault = true, Classes = { "primary" } };
        close.Click += (_, _) => Close();
        _page.Content = new StackPanel
        {
            Spacing = JGSpacing.M,
            Children = { Text(L.T("jGlancer was removed."), "heading", JGColor.TextPrimary), Buttons(close) },
        };
    }

    // MARK: Shared

    /// <summary>
    /// The program being replaced or removed must not run: asks to close it (jGlancer in the notification area too)
    /// until it is closed or the user gives up.
    /// </summary>
    private async Task<bool> WaitForClosedAsync(string executable)
    {
        while (Running(executable))
        {
            var answer = new TaskCompletionSource<bool>();
            var retry = new Button { Content = L.T("Try Again"), IsDefault = true, Classes = { "primary" } };
            retry.Click += (_, _) => answer.TrySetResult(true);
            var cancel = new Button { Content = L.T("Cancel"), IsCancel = true };
            cancel.Click += (_, _) => answer.TrySetResult(false);
            var previous = _page.Content;
            _page.Content = new StackPanel
            {
                Spacing = JGSpacing.M,
                Children =
                {
                    Text(L.T("Close jGlancer first."), "heading", JGColor.TextPrimary),
                    Text(L.T("It is still running (look in the notification area too)."), "body", JGColor.TextSecondary),
                    Buttons(cancel, retry),
                },
            };
            if (!await answer.Task)
            {
                _page.Content = previous;
                return false;
            }
            _page.Content = previous;
        }
        return true;
    }

    private static bool Running(string executable)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            using (process)
            {
                try
                {
                    if (process.Id != Environment.ProcessId
                        && string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                }
            }
        }
        return false;
    }

    private void ShowProblem(string title, string detail)
    {
        var close = new Button { Content = L.T("Close"), IsDefault = true, Classes = { "primary" } };
        close.Click += (_, _) => Close();
        var panel = new StackPanel { Spacing = JGSpacing.M, Children = { Text(title, "heading", JGColor.TextPrimary) } };
        if (detail.Length > 0)
        {
            panel.Children.Add(Text(detail, "mono", JGColor.Danger));
        }
        panel.Children.Add(Buttons(close));
        _page.Content = panel;
    }

    private Button Cancel()
    {
        var cancel = new Button { Content = L.T("Cancel"), IsCancel = true };
        cancel.Click += (_, _) => Close();
        return cancel;
    }

    private static StackPanel Buttons(params Button[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = JGSpacing.Xs };
        foreach (var button in buttons)
        {
            row.Children.Add(button);
        }
        return row;
    }

    private TextBlock Label(string text) => Text(text, "label", JGColor.TextTertiary);

    private TextBlock PathText(string path)
    {
        var text = Text(path, "mono", JGColor.TextSecondary);
        text.TextTrimming = TextTrimming.PrefixCharacterEllipsis;
        text.TextWrapping = TextWrapping.NoWrap;
        text.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(text, path);
        return text;
    }

    private TextBlock Text(string text, string style, JGColorToken color)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Classes = { style } };
        block[!TextBlock.ForegroundProperty] = this.GetResourceObservable(color.ResourceKey).ToBinding();
        return block;
    }
}
