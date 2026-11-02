using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using JGlancer.App.Services;
using JGlancer.Core.Model;
using JGlancer.Core.Scanning;

namespace JGlancer.App.Preview;

/// <summary>
/// Quick Preview for files from Explorer (macOS D-63, D-69; W11): the context menu's "Quick Preview in jGlancer" and
/// Space in Explorer. With one file the arrows go through the media files of its folder (shallow, natural order), with
/// several through those; the files are stand-ins (nothing is written to the library). Its own selection, preview and
/// window, apart from the library's: it works with no library window at all.
/// </summary>
internal sealed class ExplorerPreview : IDisposable
{
    private readonly AppServices _services;
    private readonly SelectionModel _selection = new();
    private readonly PreviewModel _preview;
    private readonly QuickPreviewWindow _window;
    private List<long> _ids = [];
    private int _index;

    public ExplorerPreview(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _preview = new PreviewModel(services, _selection);
        // No pane here: only the window shows the file.
        _preview.SetPaneShown(false);
        _window = new QuickPreviewWindow(_preview);
        _window.Dismissed += (_, _) =>
        {
            _preview.SetQuickPreviewOpen(false);
            Dismissed?.Invoke(this, EventArgs.Empty);
        };
        _window.ArrowPressed += (_, key) => Step(key is Key.Left or Key.Up ? -1 : 1);
        _window.OpenRequested += (_, _) =>
        {
            if (CurrentPath is { } path)
            {
                // The window comes first: a preview going away with nothing else open ends the app.
                OpenRequested?.Invoke(this, path);
                _window.Dismiss();
            }
        };
    }

    /// <summary>Open in Viewer: the app opens this file in its window (the viewer alone if the library is not loaded).</summary>
    public event EventHandler<string>? OpenRequested;

    /// <summary>The window went away (an app launched only for it quits then).</summary>
    public event EventHandler? Dismissed;

    public bool IsOpen => _window.IsVisible;

    /// <summary>The file shown (Space in Explorer follows its selection).</summary>
    public string? CurrentPath => _preview.Asset?.Path;

    /// <summary>
    /// Shows <paramref name="focus"/> among <paramref name="files"/> (several selected in Explorer), or among its folder's
    /// media files when it is the only one. <paramref name="activate"/> false keeps the keyboard where it is (Space in
    /// Explorer: Explorer's arrows move its selection and the preview follows).
    /// </summary>
    public async Task ShowAsync(IReadOnlyList<string> files, string focus, bool activate)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(focus);
        var paths = files.Count > 1
            ? [.. files.Where(f => MediaClassifier.Classify(f) is not null)]
            : await Task.Run(() => FolderSiblings.Of(focus, kind: null));
        _ids = [.. paths.Select(_services.StandIns.Add).OfType<long>()];
        if (_ids.Count == 0 || _services.StandIns.Add(focus) is not { } focusId)
        {
            return;
        }
        _index = Math.Max(0, _ids.IndexOf(focusId));
        _selection.Select(_ids[_index]);
        if (!_window.IsVisible)
        {
            Place();
            _window.ShowActivated = activate;
            _preview.SetQuickPreviewOpen(true);
            _window.Show();
        }
        if (activate)
        {
            _window.Activate();
        }
    }

    /// <summary>Shows another file of the same list (Explorer's selection moved); false when it is not in it.</summary>
    public bool Follow(string path)
    {
        if (_services.StandIns.Add(path) is { } id && _ids.IndexOf(id) is >= 0 and var index)
        {
            _index = index;
            _selection.Select(id);
            return true;
        }
        return false;
    }

    public void Dismiss()
    {
        if (_window.IsVisible)
        {
            _window.Dismiss();
        }
    }

    private void Step(int direction)
    {
        var next = _index + direction;
        if (next >= 0 && next < _ids.Count)
        {
            _index = next;
            _selection.Select(_ids[next]);
        }
    }

    /// <summary>Where it was last, else in the middle of the screen with the pointer.</summary>
    private void Place()
    {
        if (_window.LastFrame is { } frame)
        {
            _window.Position = new PixelPoint(frame.X, frame.Y);
            _window.Width = frame.Width;
            _window.Height = frame.Height;
            return;
        }
        if (_window.Screens.Primary is { } screen)
        {
            var area = screen.WorkingArea;
            var scaling = screen.Scaling;
            _window.Position = new PixelPoint(
                area.X + (int)((area.Width - _window.Width * scaling) / 2),
                area.Y + (int)((area.Height - _window.Height * scaling) / 2));
        }
    }

    public void Dispose()
    {
        _window.AllowsClose = true;
        _window.Close();
        _preview.Dispose();
    }
}
