using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JGlancer.App.Library.Grid;
using JGlancer.App.Settings;
using JGlancer.Core.Actions;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.Shell;

/// <summary>
/// The keyboard shortcuts of the window as Settings → Shortcuts has them (macOS ShortcutMap): the file commands act
/// on <see cref="CurrentTarget"/>, the library commands on the library. Shortcuts with Ctrl or Alt work anywhere in
/// the window; plain keys (F2, Delete) only on the grid's selection or in a viewer, never while a field has the
/// typing. The registry's fixed shortcuts (rotate, edit, copy) come after the user's. The flag key starts a chord
/// (W-05, macOS FlagChord): a digit within <see cref="JGMetrics.FlagChordWindow"/> picks the color (1–6) or removes
/// the flag (0); with no digit the accent flag is set.
/// </summary>
internal sealed partial class MainWindow
{
    private DispatcherTimer? _flagChord;

    private async void OnShortcutKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || ActiveModel is not { } model)
        {
            return;
        }
        var plain = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) == 0;
        var focused = FocusManager?.GetFocusedElement() as Avalonia.Visual;
        if (plain && focused is TextBox)
        {
            return;
        }
        if (_flagChord?.IsEnabled == true && plain && ChordDigit(e.Key) is { } digit)
        {
            _flagChord.Stop();
            e.Handled = true;
            await SetFlagAsync(digit == 0 ? AssetFlag.None : AssetFlags.Colors[digit - 1], continuesChord: true);
            return;
        }
        var map = SettingsStore.Current.Shortcuts;
        var viewerOpen = IsViewerOpen;
        // Plain keys belong to the grid while it has the focus, and to the viewers.
        var plainReachesFiles = viewerOpen || focused?.FindAncestorOfType<LibraryGrid>(includeSelf: true) is not null;
        foreach (var command in Enum.GetValues<ShortcutCommand>())
        {
            if (map.Shortcut(command) is not { } shortcut || !Matches(shortcut, e))
            {
                continue;
            }
            if (command.Action() is { } id)
            {
                if (plain && !plainReachesFiles)
                {
                    return;
                }
                if (await PerformShortcutAsync(id, e))
                {
                    return;
                }
                continue;
            }
            if (command.Flag() is { } flag)
            {
                if ((plain && !plainReachesFiles) || _actions is null || !_actions.IsAvailable(ActionId.Flag, CurrentTarget()))
                {
                    return;
                }
                e.Handled = true;
                if (command == ShortcutCommand.FlagAccent)
                {
                    // The accent shows at once; a digit right after replaces it.
                    StartFlagChord();
                }
                await SetFlagAsync(flag);
                return;
            }
            if (command == ShortcutCommand.AddFolder || !viewerOpen)
            {
                e.Handled = true;
                await RunLibraryCommandAsync(command, model);
                return;
            }
        }
        if (plain)
        {
            return;
        }
        foreach (var id in Enum.GetValues<ActionId>())
        {
            if (ShortcutCommands.Of(id) is null && id != ActionId.RemoveFromAlbum
                && ActionRegistry.Shortcut(id) is { IsMenuBarEquivalent: true } shortcut && Matches(shortcut, e)
                && await PerformShortcutAsync(id, e))
            {
                return;
            }
        }
    }

    private void StartFlagChord()
    {
        _flagChord ??= new DispatcherTimer(TimeSpan.FromSeconds(JGMetrics.FlagChordWindow), DispatcherPriority.Input, (_, _) => _flagChord!.Stop());
        _flagChord.Stop();
        _flagChord.Start();
    }

    /// <summary>The digit of a chord key, top row or keypad (by position, so any keyboard layout); null for other keys.</summary>
    private static int? ChordDigit(Key key) => key switch
    {
        >= Key.D0 and <= Key.D6 => key - Key.D0,
        >= Key.NumPad0 and <= Key.NumPad6 => key - Key.NumPad0,
        _ => null,
    };

    private async Task SetFlagAsync(AssetFlag flag, bool continuesChord = false)
    {
        if (_actions is not null && CurrentTarget() is { } target && _actions.IsAvailable(ActionId.Flag, target))
        {
            await _actions.SetFlagAsync(target, flag, continuesChord);
        }
    }

    /// <summary>Runs <paramref name="id"/> when it applies to the current target; in an album the Recycle Bin's key takes files out of it.</summary>
    private async Task<bool> PerformShortcutAsync(ActionId id, KeyEventArgs e)
    {
        if (_actions is null || CurrentTarget() is not { } target)
        {
            return false;
        }
        if (id == ActionId.MoveToTrash && target.Place == ActionPlace.Album)
        {
            id = ActionId.RemoveFromAlbum;
        }
        if (!_actions.IsAvailable(id, target))
        {
            return false;
        }
        e.Handled = true;
        await _actions.PerformAsync(id, target);
        return true;
    }

    private async Task RunLibraryCommandAsync(ShortcutCommand command, Library.LibraryModel model)
    {
        switch (command)
        {
            case ShortcutCommand.AddFolder:
                await Library.AddFolderAsync();
                break;
            case ShortcutCommand.Find when model.HasRoots:
                model.RequestSearchFocus();
                break;
            case ShortcutCommand.NewAlbum when model.HasRoots:
                // The selected files go straight into the new album (macOS).
                await model.CreateAlbumAsync(model.SelectedInOrder());
                break;
            case ShortcutCommand.TogglePreview:
                // One preview pane for the window: the main model holds it.
                _model?.SetPreviewVisible(!_model.State.PreviewVisible);
                break;
            case ShortcutCommand.ShowNames:
                model.SetShowNames(!model.State.ShowNames);
                break;
            case ShortcutCommand.SplitView:
                ToggleSplit();
                break;
            case ShortcutCommand.SplitSideBySide:
                SetSplit(Split == Core.Model.SplitMode.SideBySide ? Core.Model.SplitMode.Off : Core.Model.SplitMode.SideBySide);
                break;
            case ShortcutCommand.SplitStacked:
                SetSplit(Split == Core.Model.SplitMode.Stacked ? Core.Model.SplitMode.Off : Core.Model.SplitMode.Stacked);
                break;
            case ShortcutCommand.IncludeSubfolders:
                model.SetIncludeSubfolders(!model.State.IncludeSubfolders);
                break;
        }
    }

    private static bool Matches(ActionShortcut shortcut, KeyEventArgs e)
    {
        try
        {
            return KeyGesture.Parse(shortcut.Gesture).Matches(e);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
