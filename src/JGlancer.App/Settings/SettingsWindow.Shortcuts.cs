using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using JGlancer.Core.Actions;
using JGlancer.Core.Localization;
using JGlancer.Design;

namespace JGlancer.App.Settings;

/// <summary>
/// Settings → Shortcuts (macOS ShortcutsSettings): every customizable command with its shortcut. Click a shortcut and
/// press the new keys (with Ctrl or Alt; function keys and Delete also alone); Backspace removes it, Esc cancels. A
/// shortcut another command had moves over, and the line under the list says so.
/// </summary>
internal sealed partial class SettingsWindow
{
    private ShortcutCommand? _recording;
    private string? _shortcutNote;

    private List<Control> Shortcuts()
    {
        var note = SettingsUi.Text(_shortcutNote ?? L.T("Click a shortcut, then press the keys. Backspace removes it."), "body",
            _shortcutNote is null ? JGColor.TextTertiary : JGColor.TextSecondary, wraps: true);
        var restore = new Button { Content = L.T("Restore Defaults"), Classes = { "tertiary" }, IsEnabled = !_store.Shortcuts.IsDefault };
        restore.Click += (_, _) =>
        {
            _shortcutNote = null;
            _store.UpdateShortcuts(map =>
            {
                map.ResetAll();
                return true;
            });
        };
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = JGSpacing.S };
        Grid.SetColumn(restore, 1);
        footer.Children.Add(note);
        footer.Children.Add(restore);
        var flags = ShortcutGroup(L.T("Flags"), ShortcutCommands.Flags);
        if (_store.Shortcuts.Shortcut(ShortcutCommand.FlagAccent) is { } accent)
        {
            // The accent's key also starts the chord (W-05).
            flags.Children.Add(SettingsUi.Text(L.T("{0} then 1–6 picks a color, {0} then 0 removes the flag.", Display(accent)),
                "body", JGColor.TextTertiary, wraps: true));
        }
        return [ShortcutGroup(L.T("Files"), ShortcutCommands.Files), flags, ShortcutGroup(L.T("Library"), ShortcutCommands.Library), footer];
    }

    private StackPanel ShortcutGroup(string title, IReadOnlyList<ShortcutCommand> commands)
    {
        var rows = new StackPanel();
        for (var i = 0; i < commands.Count; i++)
        {
            if (i > 0)
            {
                rows.Children.Add(SettingsUi.Hairline());
            }
            rows.Children.Add(ShortcutRow(commands[i]));
        }
        return SettingsUi.Group(title, rows);
    }

    private Grid ShortcutRow(ShortcutCommand command)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = JGSpacing.S, MinHeight = JGMetrics.ControlHeight };
        row.Children.Add(SettingsUi.Text(CommandName(command)));
        if (_store.Shortcuts.IsCustomized(command))
        {
            var reset = SettingsUi.IconButton(JGIconData.RotateCcw, L.T("Restore Default"), () =>
            {
                _shortcutNote = null;
                _store.UpdateShortcuts(map =>
                {
                    map.Reset(command);
                    return true;
                });
            }, JGColor.TextTertiary);
            Grid.SetColumn(reset, 1);
            row.Children.Add(reset);
        }
        var shortcut = _store.Shortcuts.Shortcut(command);
        var recording = _recording == command;
        var cap = new Button
        {
            Content = SettingsUi.Text(recording ? L.T("Press keys…") : shortcut is null ? "—" : Display(shortcut),
                recording ? "body" : "mono", recording ? JGColor.AccentText : shortcut is null ? JGColor.TextTertiary : JGColor.TextPrimary),
            MinWidth = JGMetrics.ShortcutCapWidth,
            MinHeight = JGMetrics.ControlHeightCompact,
            CornerRadius = new CornerRadius(JGMetrics.RadiusSmall),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (recording)
        {
            cap.Bind(Button.BorderBrushProperty, cap.GetResourceObservable(JGColor.Accent.ResourceKey));
            cap.BorderThickness = new Thickness(JGMetrics.SelectionEdgeWidth);
        }
        AutomationProperties.SetName(cap, CommandName(command));
        cap.Click += (_, _) =>
        {
            _recording = recording ? null : command;
            Rebuild();
        };
        Grid.SetColumn(cap, 2);
        row.Children.Add(cap);
        return row;
    }

    /// <summary>While a shortcut is being recorded, the next key press is it (before the menus and the window see it).</summary>
    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_recording is not { } command)
        {
            return;
        }
        e.Handled = true;
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
        {
            return;
        }
        var plain = e.KeyModifiers == KeyModifiers.None;
        if (plain && e.Key == Key.Escape)
        {
            _recording = null;
            Rebuild();
            return;
        }
        ActionShortcut? shortcut = plain && e.Key == Key.Back ? null : new ActionShortcut(e.Key.ToString(), Modifiers(e.KeyModifiers));
        var outcome = _store.UpdateShortcuts(map => (map.Assign(shortcut, command, out var takenFrom), takenFrom));
        _shortcutNote = outcome switch
        {
            (ShortcutMap.Outcome.Assigned, { } from) => L.T("Taken from “{0}”, which now has none.", CommandName(from)),
            (ShortcutMap.Outcome.NeedsControlOrAlt, _) => L.T("Use Ctrl or Alt with the key."),
            (ShortcutMap.Outcome.Reserved, _) => L.T("Windows or the app already uses these keys."),
            _ => null,
        };
        _recording = null;
        Rebuild();
    }

    private static ActionModifiers Modifiers(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Control) ? ActionModifiers.Control : ActionModifiers.None)
        | (modifiers.HasFlag(KeyModifiers.Alt) ? ActionModifiers.Alt : ActionModifiers.None)
        | (modifiers.HasFlag(KeyModifiers.Shift) ? ActionModifiers.Shift : ActionModifiers.None);

    /// <summary>As the menus show it: <c>Ctrl+Alt+R</c>, <c>F2</c>, <c>Delete</c>.</summary>
    public static string Display(ActionShortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        try
        {
            return JGConverters.Display(KeyGesture.Parse(shortcut.Gesture));
        }
        catch (ArgumentException)
        {
            return shortcut.Gesture;
        }
    }

    /// <summary>The command's name; the flag commands by their color (the accent's key also starts the digit chord).</summary>
    public static string CommandName(ShortcutCommand command) => command.Flag() is { } flag ? Library.FlagStyle.Title(flag) : command switch
    {
        ShortcutCommand.Find => L.T("Find"),
        ShortcutCommand.EditInPrimaryApp => L.T("Edit in Primary App"),
        ShortcutCommand.MoveTo => L.T("Move to…"),
        ShortcutCommand.CopyTo => L.T("Copy to…"),
        ShortcutCommand.Rename => L.T("Rename…"),
        ShortcutCommand.SetAlbumCover => L.T("Use as Album Cover"),
        ShortcutCommand.GetInfo => L.T("Get Info"),
        ShortcutCommand.Compare => L.T("Compare"),
        ShortcutCommand.ShowInExplorer => L.T("Show in Explorer"),
        ShortcutCommand.CopyPath => L.T("Copy Path"),
        ShortcutCommand.MoveToTrash => L.T("Move to Recycle Bin"),
        ShortcutCommand.AddFolder => L.T("Add Folder…"),
        ShortcutCommand.NewAlbum => L.T("New Album"),
        ShortcutCommand.TogglePreview => L.T("Show Preview"),
        ShortcutCommand.ShowNames => L.T("Show Names"),
        ShortcutCommand.SplitView => L.T("Split View"),
        ShortcutCommand.SplitSideBySide => L.T("Side by Side"),
        ShortcutCommand.SplitStacked => L.T("Stacked"),
        _ => L.T("Include Subfolders"),
    };
}
