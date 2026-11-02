using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using JGlancer.App.Settings;
using JGlancer.Core.Localization;
using JGlancer.Core.Stems;
using JGlancer.Design;
using JGlancer.Stems;

namespace JGlancer.App.Stems;

/// <summary>
/// The model a split needs is not there yet (W-85): the app asks before it downloads anything (its only network use,
/// approved for this). Download shows the bytes received with Stop; the file is checked (SHA-256) and
/// only then kept. Answers whether the model is there afterwards.
/// </summary>
internal sealed class StemModelWindow : Window
{
    /// <summary>One client for the app's downloads (only this one).</summary>
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(30) };

    private readonly StemModelInfo _model;
    private readonly string _target;
    private readonly StemModelSource _source;
    private readonly TextBlock _line;
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1, IsVisible = false, Height = JGMetrics.OutlineWidth * 2, MinHeight = 0 };
    private readonly Button _download = new() { Content = L.T("Download"), IsDefault = true, Classes = { "primary" } };
    private readonly Button _cancel = new() { Content = L.T("Cancel"), IsCancel = true };
    private CancellationTokenSource? _running;

    private StemModelWindow(StemModelInfo model, string folder)
    {
        _model = model;
        _target = StemModelDownload.PathIn(folder, model);
        _source = StemModelDownload.Source(model);
        Title = L.T("Download the Stem Model");
        Width = JGMetrics.DialogWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = this.GetResourceObservable(JGColor.BgPanel.ResourceKey).ToBinding();
        JGWindowChrome.Apply(this);
        var heading = SettingsUi.Text(Title, "heading", JGColor.TextPrimary);
        var stems = model.Mode == StemMode.Six ? 6 : 4;
        var text = SettingsUi.Text(L.T("Splitting into {0} stems needs the Demucs model ({1}), downloaded once from GitHub. Nothing else is sent or received: your music stays on this computer.",
            stems, Core.Localization.ByteFormat.Format(_source.Bytes)), "body", JGColor.TextSecondary, wraps: true);
        _line = SettingsUi.Text("", "mono", JGColor.TextSecondary);
        _line.IsVisible = false;
        _bar[!ProgressBar.ForegroundProperty] = this.GetResourceObservable(JGColor.Accent.ResourceKey).ToBinding();
        _bar[!ProgressBar.BackgroundProperty] = this.GetResourceObservable(JGColor.LineHair.ResourceKey).ToBinding();
        _cancel.Click += (_, _) =>
        {
            if (_running is { } running)
            {
                running.Cancel();
            }
            else
            {
                Close(false);
            }
        };
        _download.Click += async (_, _) => await DownloadAsync();
        Content = new StackPanel
        {
            Margin = new Thickness(JGSpacing.L),
            Spacing = JGSpacing.S,
            Children =
            {
                heading,
                text,
                _bar,
                _line,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = JGSpacing.Xs,
                    Margin = new Thickness(0, JGSpacing.S, 0, 0),
                    Children = { _cancel, _download },
                },
            },
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            }
        };
        Closing += (_, _) => _running?.Cancel();
    }

    /// <summary>True when the model is there (already, or downloaded now).</summary>
    public static async Task<bool> EnsureAsync(Window owner, StemModelInfo model, string folder)
    {
        if (StemModelDownload.IsInstalled(folder, model))
        {
            return true;
        }
        await new StemModelWindow(model, folder).ShowDialog<bool?>(owner);
        return StemModelDownload.IsInstalled(folder, model);
    }

    private async Task DownloadAsync()
    {
        using var running = new CancellationTokenSource();
        _running = running;
        _download.IsEnabled = false;
        _cancel.Content = L.T("Stop");
        _bar.IsVisible = true;
        _line.IsVisible = true;
        _bar.Value = 0;
        var total = Core.Localization.ByteFormat.Format(_source.Bytes);
        _line.Text = L.T("{0} of {1}", Core.Localization.ByteFormat.Format(0), total);
        var shown = 0L;
        try
        {
            await Task.Run(() => StemModelDownload.DownloadAsync(Client, _source, _target, received =>
            {
                // A few updates a second are enough.
                if (received - Interlocked.Read(ref shown) < 512 * 1024 && received < _source.Bytes)
                {
                    return;
                }
                Interlocked.Exchange(ref shown, received);
                Dispatcher.UIThread.Post(() =>
                {
                    _bar.Value = (double)received / _source.Bytes;
                    _line.Text = L.T("{0} of {1}", Core.Localization.ByteFormat.Format(received), total);
                });
            }, running.Token));
            _running = null;
            Close(true);
            return;
        }
        catch (OperationCanceledException)
        {
            _line.Text = L.T("Download stopped.");
        }
        catch (InvalidDataException)
        {
            _line.Text = L.T("The download was damaged. Try again.");
        }
        catch (HttpRequestException)
        {
            _line.Text = L.T("Couldn't download the model. Check the connection and try again.");
        }
        catch (IOException)
        {
            _line.Text = L.T("Couldn't save the model.");
        }
        _running = null;
        _bar.IsVisible = false;
        _cancel.Content = L.T("Cancel");
        _download.Content = L.T("Try Again");
        _download.IsEnabled = true;
    }

    public StemModelInfo Model => _model;
}
