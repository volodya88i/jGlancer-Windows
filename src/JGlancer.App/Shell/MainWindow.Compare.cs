using JGlancer.App.Viewer;

namespace JGlancer.App.Shell;

/// <summary>
/// Compare two photos (W-77): Compare in the menu or Ctrl+Alt+C on two selected photos opens them side by side over
/// the library, as the viewers do; Esc or "‹ Library" goes back with the first one selected.
/// </summary>
internal sealed partial class MainWindow
{
    private CompareView? _compare;

    /// <summary>A viewer, a player or the compare view fills the window.</summary>
    private bool IsViewerOpen => _viewer is not null || _mediaViewer is not null || _compare is not null;

    /// <summary>The open compare view, if any (developer hooks).</summary>
    public CompareView? CompareView => _compare;

    void Actions.IActionHost.Compare(IReadOnlyList<long> assetIds) => _ = CompareAsync(assetIds);

    public async Task CompareAsync(IReadOnlyList<long> assetIds)
    {
        if (ActiveModel is not { } model || assetIds.Count != 2 || IsViewerOpen)
        {
            return;
        }
        var rows = (await model.Store.AssetsAsync(assetIds)).ToDictionary(row => row.Id);
        if (!rows.TryGetValue(assetIds[0], out var left) || !rows.TryGetValue(assetIds[1], out var right) || IsViewerOpen)
        {
            return;
        }
        if (IsQuickPreviewOpen)
        {
            _quickPreview!.Dismiss();
        }
        _preview?.Suspend();
        var view = new CompareView { Model = new CompareModel(model.Services, left, right, LargestScreen) };
        view.CloseRequested += (_, _) => OnViewerClose();
        _compare = view;
        Body.Children.Add(view);
        Library.IsVisible = false;
    }

    /// <summary>Closes the compare view; the photo that was on the left, to select in the library.</summary>
    private long? CloseCompare()
    {
        if (_compare is not { } view)
        {
            return null;
        }
        var shown = view.Assets is [var first, ..] ? first.Id : (long?)null;
        view.Model?.Dispose();
        view.Model = null;
        Body.Children.Remove(view);
        _compare = null;
        return shown;
    }
}
