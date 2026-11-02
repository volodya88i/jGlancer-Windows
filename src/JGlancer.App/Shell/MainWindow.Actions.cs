using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using JGlancer.App.Actions;
using JGlancer.App.Library;
using JGlancer.App.Services;
using JGlancer.Core.Actions;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.Shell;

/// <summary>
/// The actions of the window (macOS AppModel.currentTarget, ActionMenu): the context menus of the grid and the
/// viewers, the registry's shortcuts, and the target the menu bar acts on — the viewer's file while a viewer is open,
/// else the library selection.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly ActionPerformer? _actions;

    Window IActionHost.Window => this;

    void IActionHost.Open(long assetId) => ActiveModel?.Open(assetId);

    /// <summary>The active pane's model: album and flag actions apply to what it shows (split view).</summary>
    LibraryModel? IActionHost.Library => ActiveModel;

    /// <summary>The actions (developer hooks, the menu bar).</summary>
    public ActionPerformer? Actions => _actions;

    private void AttachActions()
    {
        Library.LibraryGrid.ContextMenuRequested += async (_, _) => await ShowContextMenuAsync(Library.LibraryGrid);
        AddHandler(PointerReleasedEvent, OnViewerPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnShortcutKey, RoutingStrategies.Bubble);
        TitleBar.AttachActions(_actions!, CurrentTarget, () =>
        {
            if (ActiveModel is { } active)
            {
                active.Selection.SelectAll(active.Order);
            }
        });
    }

    /// <summary>What the actions apply to now: the viewer's file, else the library selection; null for nothing.</summary>
    public ActionTarget? CurrentTarget()
    {
        if (ActiveModel is not { } model)
        {
            return null;
        }
        if (_compare?.Assets is [var left, var right])
        {
            return Target(ActionPlace.Compare, [left.Id, right.Id], new HashSet<MediaKind> { MediaKind.Photo }, left.Path);
        }
        if (_viewer?.Model?.CurrentId is { } photo)
        {
            return Target(ActionPlace.Viewer, [photo], new HashSet<MediaKind> { MediaKind.Photo }, _viewer.Model.Current?.Path);
        }
        if (_mediaViewer?.Model?.Current is { } item)
        {
            return item.AssetId is { } id
                ? Target(ActionPlace.Viewer, [id], new HashSet<MediaKind> { item.Kind }, item.Path)
                : null;
        }
        if (model.Selection.IsEmpty)
        {
            return null;
        }
        // Display order; kinds from the shown list.
        var ids = model.Selection.Selected
            .Select(id => (id, index: model.Order.IndexOf(id)))
            .Where(p => p.index is not null)
            .OrderBy(p => p.index)
            .Select(p => p.id)
            .ToList();
        var kinds = ids.Select(id => model.Refs[model.Order.IndexOf(id)!.Value].Kind).ToHashSet();
        // A folded image sequence stands for all of its frames (W-81).
        return ids.Count == 0 ? null : Target(model.State.AlbumId is null ? ActionPlace.Library : ActionPlace.Album, model.WithSequenceFrames(ids), kinds, null);

        ActionTarget Target(ActionPlace place, IReadOnlyList<long> assetIds, IReadOnlySet<MediaKind> kinds, string? sample) =>
            new(place, assetIds, kinds, sample, async () =>
            {
                var rows = (await model.Services.AssetsAsync(assetIds)).ToDictionary(row => row.Id, row => row.Path);
                return [.. assetIds.Where(rows.ContainsKey).Select(id => rows[id])];
            });
    }

    /// <summary>Runs <paramref name="id"/> on the current target, when it applies (viewer buttons, developer hooks).</summary>
    public async Task PerformAsync(ActionId id)
    {
        if (_actions is not null && CurrentTarget() is { } target && _actions.IsAvailable(id, target))
        {
            await _actions.PerformAsync(id, target);
        }
    }

    /// <summary>The context menu of the current target over <paramref name="anchor"/>, at the pointer.</summary>
    private async Task<ContextMenu?> ShowContextMenuAsync(Control anchor, PlacementMode placement = PlacementMode.Pointer)
    {
        if (_actions is null || _model is null || CurrentTarget() is not { } target)
        {
            return null;
        }
        if (target.Sample is null)
        {
            // The Open With suggestions are for the type of the focused (or first) file.
            var sampleId = ActiveModel?.Selection.Focus is { } focus && target.AssetIds.Contains(focus) ? focus : target.AssetIds[0];
            var rows = await _model.Services.AssetsAsync([sampleId]);
            target = target with { Sample = rows.Count > 0 ? rows[0].Path : null };
        }
        target = target with { Memberships = await _model.Store.AlbumMembershipsAsync(target.AssetIds) };
        if (ActionMenu.Make(target, _actions, RenderScaling) is not { } menu)
        {
            return null;
        }
        menu.Placement = placement;
        menu.Open(anchor);
        return menu;
    }

    /// <summary>`--context-menu`: the selection's menu over the grid's middle, Open With unfolded.</summary>
    public async Task ShowContextMenuForDeveloperAsync()
    {
        if (await ShowContextMenuAsync(Library.LibraryGrid, PlacementMode.Center) is { } menu
            && menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.ItemCount > 0) is { } openWith)
        {
            openWith.IsSubMenuOpen = true;
        }
    }

    /// <summary>A right click in a viewer (not on its scrubber, which has its own menu) shows the file's actions.</summary>
    private async void OnViewerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right || (_viewer is null && _mediaViewer is null)
            || e.Source is not Visual source || source.FindAncestorOfType<JGScrubber>(includeSelf: true) is not null
            || source.FindAncestorOfType<ContextMenu>(includeSelf: true) is not null)
        {
            return;
        }
        await ShowContextMenuAsync((Control?)_viewer ?? _mediaViewer!);
    }
}
