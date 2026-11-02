using System.Text.Json;
using System.Text.Json.Serialization;
using JGlancer.Core.Model;

namespace JGlancer.Core.Actions;

/// <summary>
/// The commands whose shortcut the user can change (Settings → Shortcuts; macOS ShortcutCommand). Keys that belong to a
/// view — Enter and Space in the grid, arrows, the players' keys — are not here. Names are persisted.
/// </summary>
public enum ShortcutCommand
{
    // Files (the registry's actions)
    EditInPrimaryApp,
    MoveTo,
    CopyTo,
    Rename,
    SetAlbumCover,
    GetInfo,
    ShowInExplorer,
    CopyPath,
    Compare,
    MoveToTrash,

    // Flags (macOS D-57, W-05)
    FlagAccent,
    FlagRed,
    FlagOrange,
    FlagYellow,
    FlagGreen,
    FlagBlue,
    FlagPurple,
    RemoveFlag,

    // Library
    Find,
    AddFolder,
    NewAlbum,
    TogglePreview,
    ShowNames,
    IncludeSubfolders,

    // Split view: on and off, side by side, stacked.
    SplitView,
    SplitSideBySide,
    SplitStacked,
}

public static class ShortcutCommands
{
    /// <summary>The registry action with the same name, for the file commands.</summary>
    public static ActionId? Action(this ShortcutCommand command) =>
        Enum.TryParse<ActionId>(command.ToString(), out var id) ? id : null;

    public static IReadOnlyList<ShortcutCommand> Files { get; } = [.. Enum.GetValues<ShortcutCommand>().Where(c => c.Action() is not null)];

    public static IReadOnlyList<ShortcutCommand> Flags { get; } = [.. Enum.GetValues<ShortcutCommand>().Where(c => c.Flag() is not null)];

    public static IReadOnlyList<ShortcutCommand> Library { get; } = [.. Enum.GetValues<ShortcutCommand>().Where(c => c.Action() is null && c.Flag() is null)];

    /// <summary>The flag a flag command sets (<see cref="AssetFlag.None"/> for Remove Flag); null for other commands.</summary>
    public static AssetFlag? Flag(this ShortcutCommand command) => command switch
    {
        ShortcutCommand.FlagAccent => AssetFlag.Accent,
        ShortcutCommand.FlagRed => AssetFlag.Red,
        ShortcutCommand.FlagOrange => AssetFlag.Orange,
        ShortcutCommand.FlagYellow => AssetFlag.Yellow,
        ShortcutCommand.FlagGreen => AssetFlag.Green,
        ShortcutCommand.FlagBlue => AssetFlag.Blue,
        ShortcutCommand.FlagPurple => AssetFlag.Purple,
        ShortcutCommand.RemoveFlag => AssetFlag.None,
        _ => null,
    };

    /// <summary>The command that sets <paramref name="flag"/>.</summary>
    public static ShortcutCommand CommandFor(AssetFlag flag) =>
        Enum.GetValues<ShortcutCommand>().FirstOrDefault(c => c.Flag() == flag, ShortcutCommand.RemoveFlag);

    /// <summary>The command of a registry action, if its shortcut can be changed.</summary>
    public static ShortcutCommand? Of(ActionId id) => Enum.TryParse<ShortcutCommand>(id.ToString(), out var command) ? command : null;

    /// <summary>Defaults: the registry's (Explorer's keys where it has one, W-45), and the library's own.</summary>
    public static ActionShortcut? Default(this ShortcutCommand command) => command switch
    {
        ShortcutCommand.Find => new("F", ActionModifiers.Control),
        // F alone flags with the accent; F then 1–6 picks a color, F then 0 removes (the app's flag chord, W-05).
        // The colors and Remove Flag have no shortcut of their own unless the user gives them one.
        ShortcutCommand.FlagAccent => new("F"),
        ShortcutCommand.FlagRed or ShortcutCommand.FlagOrange or ShortcutCommand.FlagYellow or ShortcutCommand.FlagGreen
            or ShortcutCommand.FlagBlue or ShortcutCommand.FlagPurple or ShortcutCommand.RemoveFlag => null,
        ShortcutCommand.AddFolder => new("O", ActionModifiers.Control),
        ShortcutCommand.NewAlbum => new("N", ActionModifiers.Control),
        ShortcutCommand.TogglePreview => new("D0", ActionModifiers.Control | ActionModifiers.Alt),
        ShortcutCommand.ShowNames or ShortcutCommand.IncludeSubfolders => null,
        ShortcutCommand.SplitView => new("S", ActionModifiers.Control | ActionModifiers.Alt),
        ShortcutCommand.SplitSideBySide => new("V", ActionModifiers.Control | ActionModifiers.Alt),
        ShortcutCommand.SplitStacked => new("H", ActionModifiers.Control | ActionModifiers.Alt),
        _ => command.Action() is { } id ? ActionRegistry.Shortcut(id) : null,
    };
}

/// <summary>
/// The user's shortcuts as changes on top of the defaults (macOS ShortcutMap), saved as one value. A shortcut needs
/// Ctrl or Alt, except the function keys and Delete (as Explorer's F2 and Delete): a plain key would be taken from
/// typing, the grid and the players.
/// </summary>
public sealed class ShortcutMap
{
    /// <summary>Commands with a shortcut other than their default (JSON).</summary>
    public Dictionary<ShortcutCommand, ActionShortcut> Custom { get; set; } = [];

    /// <summary>Commands whose shortcut was removed (JSON).</summary>
    public HashSet<ShortcutCommand> Cleared { get; set; } = [];

    public enum Outcome
    {
        Assigned,

        /// <summary>A letter, digit or sign without Ctrl or Alt.</summary>
        NeedsControlOrAlt,

        /// <summary>Windows or the app uses it already (Copy, Undo, Settings, the viewer's zoom…).</summary>
        Reserved,
    }

    /// <summary>Kept by Windows or by views of the app; never assignable.</summary>
    public static IReadOnlySet<ActionShortcut> Reserved { get; } = new HashSet<ActionShortcut>
    {
        new("C", ActionModifiers.Control), new("V", ActionModifiers.Control), new("X", ActionModifiers.Control),
        new("A", ActionModifiers.Control), new("Z", ActionModifiers.Control), new("Y", ActionModifiers.Control),
        new("Z", ActionModifiers.Control | ActionModifiers.Shift), new("OemComma", ActionModifiers.Control),
        new("F4", ActionModifiers.Alt), new("F11"), new("F10"), new("F1"), new("Escape"), new("Enter"), new("Space"),
        new("Tab", ActionModifiers.Control), new("Tab", ActionModifiers.Alt),
        // The photo viewer's zoom (Ctrl+0 fit, Ctrl+1 actual size, Ctrl+9 fill, Ctrl+plus / minus).
        new("D0", ActionModifiers.Control), new("D1", ActionModifiers.Control), new("D9", ActionModifiers.Control),
        new("OemPlus", ActionModifiers.Control), new("OemMinus", ActionModifiers.Control),
        // The players' previous / next file.
        new("Left", ActionModifiers.Alt), new("Right", ActionModifiers.Alt),
    };

    public ActionShortcut? Shortcut(ShortcutCommand command) =>
        Cleared.Contains(command) ? null : Custom.TryGetValue(command, out var custom) ? custom : command.Default();

    public bool IsCustomized(ShortcutCommand command) => Custom.ContainsKey(command) || Cleared.Contains(command);

    public bool IsDefault => Custom.Count == 0 && Cleared.Count == 0;

    /// <summary>The command <paramref name="shortcut"/> runs, if any.</summary>
    public ShortcutCommand? CommandFor(ActionShortcut shortcut) =>
        Enum.GetValues<ShortcutCommand>().Cast<ShortcutCommand?>().FirstOrDefault(c => Shortcut(c!.Value) == shortcut);

    /// <summary>Gives <paramref name="command"/> the shortcut (null removes it); one held by another command moves over (<paramref name="takenFrom"/>).</summary>
    public Outcome Assign(ActionShortcut? shortcut, ShortcutCommand command, out ShortcutCommand? takenFrom)
    {
        takenFrom = null;
        if (shortcut is null)
        {
            Store(null, command);
            return Outcome.Assigned;
        }
        if (!IsAllowedWithoutModifier(shortcut.Key) && (shortcut.Modifiers & (ActionModifiers.Control | ActionModifiers.Alt)) == 0)
        {
            return Outcome.NeedsControlOrAlt;
        }
        if (Reserved.Contains(shortcut))
        {
            return Outcome.Reserved;
        }
        takenFrom = Enum.GetValues<ShortcutCommand>().Cast<ShortcutCommand?>().FirstOrDefault(c => c != command && Shortcut(c!.Value) == shortcut);
        if (takenFrom is { } holder)
        {
            Store(null, holder);
        }
        Store(shortcut, command);
        return Outcome.Assigned;
    }

    /// <summary>Back to the default; if another command holds the default now, it is not taken from it.</summary>
    public void Reset(ShortcutCommand command)
    {
        Custom.Remove(command);
        Cleared.Remove(command);
        if (command.Default() is { } shortcut && Enum.GetValues<ShortcutCommand>().Any(c => c != command && Shortcut(c) == shortcut))
        {
            Cleared.Add(command);
        }
    }

    public void ResetAll()
    {
        Custom.Clear();
        Cleared.Clear();
    }

    /// <summary>Function keys and Delete work alone (F2 renames, Delete sends to the Recycle Bin).</summary>
    private static bool IsAllowedWithoutModifier(string key) =>
        key == "Delete" || (key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out var number) && number is >= 1 and <= 24);

    private void Store(ActionShortcut? shortcut, ShortcutCommand command)
    {
        if (shortcut == command.Default())
        {
            Custom.Remove(command);
            Cleared.Remove(command);
        }
        else if (shortcut is not null)
        {
            Custom[command] = shortcut;
            Cleared.Remove(command);
        }
        else
        {
            Custom.Remove(command);
            Cleared.Add(command);
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, ShortcutMapJson.Default.ShortcutMap);

    /// <summary>Reads a saved map; anything unreadable gives the defaults.</summary>
    public static ShortcutMap FromJson(string? json)
    {
        try
        {
            return json is null ? new() : JsonSerializer.Deserialize(json, ShortcutMapJson.Default.ShortcutMap) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }
}

/// <summary>Source-generated JSON (NativeAOT: no reflection).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ShortcutMap))]
internal sealed partial class ShortcutMapJson : JsonSerializerContext;
