using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using JGlancer.App.Services;
using JGlancer.App.Settings;
using JGlancer.Core.Localization;
using JGlancer.Core.Updates;
using JGlancer.Design;

namespace JGlancer.App.Shell;

/// <summary>
/// A newer version is out (W-101): its number and notes as plain text, with Update, Later and Skip This Version. Update
/// downloads the installer, checks its signature and only then starts it, and the app ends for it.
/// </summary>
internal sealed class UpdateWindow : Window
{
    private readonly AppRelease _release;
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1, IsVisible = false, Height = JGMetrics.OutlineWidth * 2, MinHeight = 0 };
    private readonly TextBlock _line = SettingsUi.Text("", "mono", JGColor.TextSecondary, wraps: true);
    private readonly Button _update = new() { Content = L.T("Update"), IsDefault = true, Classes = { "primary" } };
    private readonly Button _later = new() { Content = L.T("Later"), IsCancel = true };
    private readonly Button _skip = new() { Content = L.T("Skip This Version"), Classes = { "tertiary" } };
    private CancellationTokenSource? _running;

    private UpdateWindow(AppRelease release)
    {
        _release = release;
        Title = L.T("Update jGlancer");
        Width = JGMetrics.DialogWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = this.GetResourceObservable(JGColor.BgPanel.ResourceKey).ToBinding();
        _line.IsVisible = false;
        _bar[!ProgressBar.ForegroundProperty] = this.GetResourceObservable(JGColor.Accent.ResourceKey).ToBinding();
        _bar[!ProgressBar.BackgroundProperty] = this.GetResourceObservable(JGColor.LineHair.ResourceKey).ToBinding();
        _update.Click += async (_, _) => await UpdateAsync();
        _later.Click += (_, _) =>
        {
            if (_running is { } running)
            {
                running.Cancel();
            }
            else
            {
                Close();
            }
        };
        _skip.Click += (_, _) =>
        {
            SettingsStore.Current.Update(s => s with { SkippedUpdateVersion = release.VersionText });
            Close();
        };
        var content = new StackPanel { Margin = new Thickness(JGSpacing.L), Spacing = JGSpacing.S };
        content.Children.Add(SettingsUi.Text(L.T("jGlancer {0} is available", release.VersionText), "heading", JGColor.TextPrimary, wraps: true));
        content.Children.Add(SettingsUi.Text(L.T("You have version {0}.", AboutWindow.Version), "body", JGColor.TextSecondary));
        if (release.Notes.Length > 0)
        {
            content.Children.Add(new ScrollViewer
            {
                MaxHeight = JGMetrics.SettingsSidebarWidth,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = SettingsUi.Text(release.Notes, "body", JGColor.TextPrimary, wraps: true),
            });
        }
        content.Children.Add(SettingsUi.Text(L.T("jGlancer looks for new versions on GitHub once a day; turn this off in Settings → General."), "body", JGColor.TextTertiary, wraps: true));
        content.Children.Add(_bar);
        content.Children.Add(_line);
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, JGSpacing.S, 0, 0) };
        buttons.Children.Add(_skip);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = JGSpacing.Xs, Children = { _later, _update } };
        Grid.SetColumn(right, 2);
        buttons.Children.Add(right);
        content.Children.Add(buttons);
        Content = content;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _later.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
            }
        };
        Closing += (_, _) => _running?.Cancel();
    }

    public static Task ShowAsync(Window owner, AppRelease release) => new UpdateWindow(release).ShowDialog(owner);

    private async Task UpdateAsync()
    {
        using var running = new CancellationTokenSource();
        _running = running;
        _update.IsEnabled = false;
        _skip.IsEnabled = false;
        _later.Content = L.T("Stop");
        _bar.IsVisible = true;
        _bar.Value = 0;
        _line.IsVisible = true;
        _line.Text = L.T("Downloading…");
        var shown = 0L;
        try
        {
            var installer = await Task.Run(() => UpdateService.DownloadAsync(_release, (received, total) =>
            {
                // A few updates a second are enough.
                if (received - Interlocked.Read(ref shown) < 512 * 1024)
                {
                    return;
                }
                Interlocked.Exchange(ref shown, received);
                Dispatcher.UIThread.Post(() =>
                {
                    _bar.Value = total is > 0 ? (double)received / total.Value : 0;
                    _line.Text = total is { } size
                        ? L.T("{0} of {1}", ByteFormat.Format(received), ByteFormat.Format(size))
                        : ByteFormat.Format(received);
                });
            }, running.Token));
            _running = null;
            UpdateService.InstallAndQuit(installer);
            return;
        }
        catch (OperationCanceledException)
        {
            _line.Text = L.T("Download stopped.");
        }
        catch (InvalidDataException)
        {
            _line.Text = L.T("The download could not be verified and was deleted. Nothing was installed.");
        }
        catch (HttpRequestException)
        {
            _line.Text = L.T("Couldn't download the update. Check the connection and try again.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _line.Text = L.T("Couldn't start the update.");
        }
        _running = null;
        _bar.IsVisible = false;
        _later.Content = L.T("Later");
        _update.Content = L.T("Try Again");
        _update.IsEnabled = true;
        _skip.IsEnabled = true;
    }
}
