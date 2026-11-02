using Avalonia.Threading;
using JGlancer.Core.Scanning;
using JGlancer.Platform.Windows;

namespace JGlancer.App.Preview;

/// <summary>
/// Space in Explorer opens jGlancer's Quick Preview (Settings → Windows; W11): the hook holds Space back in Explorer's
/// file view, the selection is read here on the UI thread; a photo, video or audio file opens in the preview (Explorer
/// keeps the keyboard, so its arrows move the selection and the preview follows), anything else gets its Space back.
/// </summary>
internal sealed class ExplorerSpaceHelper : IDisposable
{
    /// <summary>How long after an arrow Explorer has moved its selection.</summary>
    private static readonly TimeSpan FollowDelay = TimeSpan.FromMilliseconds(80);

    private readonly ExplorerSpaceHook _hook = new();
    private readonly Func<IReadOnlyList<string>, string, Task<bool>> _show;
    private readonly ExplorerPreviewAccess _preview;

    /// <param name="show">Shows files in the Explorer preview without taking the keyboard; false when it could not.</param>
    public ExplorerSpaceHelper(Func<IReadOnlyList<string>, string, Task<bool>> show, ExplorerPreviewAccess preview)
    {
        _show = show;
        _preview = preview;
        _hook.SpacePressed += (window, desktop) => Dispatcher.UIThread.Post(() => _ = OnSpaceAsync(window, desktop));
        _hook.CloseRequested += () => Dispatcher.UIThread.Post(() => _preview.Dismiss());
        _hook.SelectionMoved += (window, desktop) => _ = FollowAsync(window, desktop);
        _hook.Start();
    }

    /// <summary>The preview opened or closed: Space and Esc in Explorer are for it while it is open.</summary>
    public void PreviewChanged(bool open) => _hook.PreviewOpen = open;

    private async Task OnSpaceAsync(nint window, bool desktop)
    {
        var selected = ExplorerSelection.Of(window, desktop);
        var media = selected.Where(path => MediaClassifier.Classify(path) is not null && File.Exists(path)).ToList();
        // The first selected item decides, as Explorer's own Space acts on it.
        if (selected.Count == 0 || media.Count == 0 || !string.Equals(selected[0], media[0], StringComparison.OrdinalIgnoreCase)
            || !await _show(media, media[0]))
        {
            ExplorerSpaceHook.Replay();
        }
    }

    private int _followPending;

    /// <summary>
    /// Explorer's selection moved (an arrow, a click): once it settled, the preview shows the first selected media
    /// file. A click raises several events: they make one read.
    /// </summary>
    private async Task FollowAsync(nint window, bool desktop)
    {
        if (Interlocked.Exchange(ref _followPending, 1) == 1)
        {
            return;
        }
        await Task.Delay(FollowDelay).ConfigureAwait(false);
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _followPending, 0);
            // Only while the preview is open: closing it gives Explorer the focus back, which reads as a selection
            // event, and must not open it again.
            if (!_preview.IsOpen)
            {
                return;
            }
            var selected = ExplorerSelection.Of(window, desktop);
            if (selected is [var path, ..] && MediaClassifier.Classify(path) is not null && !_preview.Follow(path))
            {
                // Another folder (or several files): the preview starts over with them.
                _ = _show([.. selected.Where(p => MediaClassifier.Classify(p) is not null)], path);
            }
        });
    }

    /// <summary>Clicking other files in Explorer while the preview is open shows them (Settings → Windows).</summary>
    public bool FollowsClicks
    {
        get => _hook.FollowsClicks;
        set => _hook.FollowsClicks = value;
    }

    public void Dispose() => _hook.Dispose();
}

/// <summary>What the helper needs of the Explorer preview (it is made by the app, maybe later than the helper).</summary>
internal sealed class ExplorerPreviewAccess(Func<ExplorerPreview?> preview)
{
    public void Dismiss() => preview()?.Dismiss();

    public bool IsOpen => preview()?.IsOpen == true;

    /// <summary>Shows <paramref name="path"/> if it is among the preview's files; false otherwise.</summary>
    public bool Follow(string path) => preview() is { IsOpen: true } open && open.Follow(path);
}
