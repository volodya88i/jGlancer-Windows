using JGlancer.App.Developer;
using JGlancer.App.Library;
using JGlancer.Core.Model;

namespace JGlancer.App.Shell;

/// <summary>
/// The split view: the library's content in two panes, side by side or stacked, each with its own
/// folder, filters, sort, tile size and selection; the second pane follows the main library model (its roots, trees,
/// albums, indexing and undo history). The pane last clicked is the active one: actions, the preview pane, Quick
/// Preview, the View menu and the sidebar's clicks apply to it. View ▸ Split View, Ctrl+Alt+S / V / H (customizable).
/// </summary>
internal sealed partial class MainWindow
{
    private LibraryModel? _secondModel;

    /// <summary>The model of the active pane (the main one without the split view).</summary>
    private LibraryModel? ActiveModel => Library.ActiveModel ?? _model;

    public SplitMode Split => _model?.State.Split ?? SplitMode.Off;

    private void AttachSplit(LibraryModel model)
    {
        Library.ActivePaneChanged += (_, _) => ActivePaneChanged();
        Library.SplitRatioChanged += (_, ratio) => model.UpdateState(s => s.SplitRatio = ratio);
        TitleBar.SplitRequested += (_, mode) => SetSplit(mode);
        TitleBar.CurrentSplit = () => Split;
        if ((DeveloperOptions.Current.Split ?? model.State.Split) is var mode and not SplitMode.Off)
        {
            SetSplit(mode);
        }
    }

    /// <summary>On and off; on again it comes back side by side or stacked, as it was last.</summary>
    public void ToggleSplit() => SetSplit(Split == SplitMode.Off ? _model?.State.LastSplit ?? SplitMode.SideBySide : SplitMode.Off);

    public void SetSplit(SplitMode mode)
    {
        if (_model is not { } model)
        {
            return;
        }
        if (mode == SplitMode.Off)
        {
            if (_secondModel is { } second)
            {
                _secondModel = null;
                Library.SetSplit(SplitMode.Off, null, model.State.SplitRatio);
                second.Stop();
            }
        }
        else
        {
            if (_secondModel is null)
            {
                var store = new ViewStateStore(model.Services.Paths.SplitViewState, !DeveloperOptions.Current.IsEphemeral);
                var second = new LibraryModel(model.Services, store, model);
                second.OpenRequested += async (_, id) => await OpenAsync(id, second);
                second.MiniPlayerRequested += (_, id) => ((Actions.IActionHost)this).PlayInMiniPlayer([id]);
                second.QuickPreviewKey += (_, e) => e.Handled = HandleQuickPreviewKey(e.Key);
                second.StartFollowing();
                _secondModel = second;
            }
            Library.SetSplit(mode, _secondModel, model.State.SplitRatio);
        }
        model.UpdateState(s =>
        {
            s.Split = mode;
            if (mode != SplitMode.Off)
            {
                s.LastSplit = mode;
            }
        });
        ActivePaneChanged();
    }

    /// <summary>Another pane is the active one: the preview and the View menu's checks follow it.</summary>
    private void ActivePaneChanged()
    {
        if (ActiveModel is { } active)
        {
            _preview?.Follow(active.Selection);
            TitleBar.Model = active;
        }
    }
}
