using JGlancer.Core.Model;

namespace JGlancer.Core.Actions;

/// <summary>
/// Every user action of the app (macOS ActionID, FEATURES §8). Names may be persisted (customized shortcuts, W9)
/// and must never change.
/// </summary>
public enum ActionId
{
    Open,
    QuickPreview,
    OpenWith,
    EditInPrimaryApp,
    RotateLeft,
    RotateRight,
    Edit,
    AddToAlbum,
    RemoveFromAlbum,
    SetAlbumCover,
    MoveTo,
    CopyTo,
    Rename,
    GetInfo,
    ShowInExplorer,
    CopyPath,
    Copy,
    MoveToTrash,

    /// <summary>The Flag submenu (colors and Remove Flag).</summary>
    Flag,

    /// <summary>Songs play in the bar at the bottom of the window while the library stays usable (W-68).</summary>
    PlayInMiniPlayer,

    /// <summary>Two photos side by side with linked zoom and pan (W-77).</summary>
    Compare,

    /// <summary>Songs split into vocals, drums, bass and the rest as separate files (W-85).</summary>
    SplitIntoStems,
}

/// <summary>Modifier keys of an <see cref="ActionShortcut"/>.</summary>
[Flags]
public enum ActionModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
}

/// <summary>A keyboard shortcut as data: a key name as Avalonia spells it (<c>Enter</c>, <c>Space</c>, <c>I</c>) and modifiers.</summary>
public sealed record ActionShortcut(string Key, ActionModifiers Modifiers = ActionModifiers.None)
{
    /// <summary>
    /// A shortcut the menu bar may own. Plain keys (Space, Enter) are only hints in menus: as hot keys they would take
    /// the key from the grid's own handling, the players and text fields.
    /// </summary>
    public bool IsMenuBarEquivalent => (Modifiers & (ActionModifiers.Control | ActionModifiers.Alt)) != 0;

    /// <summary>The gesture text Avalonia parses: <c>Ctrl+Alt+E</c>.</summary>
    public string Gesture
    {
        get
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(ActionModifiers.Control))
            {
                parts.Add("Ctrl");
            }
            if (Modifiers.HasFlag(ActionModifiers.Alt))
            {
                parts.Add("Alt");
            }
            if (Modifiers.HasFlag(ActionModifiers.Shift))
            {
                parts.Add("Shift");
            }
            parts.Add(Key);
            return string.Join('+', parts);
        }
    }
}

/// <summary>Parts of the app an action needs; a phase not built yet (or a mode, W9) leaves some out.</summary>
public enum ActionFeature
{
    OpenWith,
    FileOperations,
    Rotate,
    Editing,
    Albums,
    Flags,

    /// <summary>The window's mini player (not for files outside the library: Explorer's preview has no window).</summary>
    MiniPlayer,

    /// <summary>The stem splitter (W-85).</summary>
    StemSplitting,
}

/// <summary>Where the files of an action are.</summary>
public enum ActionPlace
{
    Library,

    /// <summary>The library grid showing an album.</summary>
    Album,
    Viewer,

    /// <summary>The two photos of the compare view (W-77): only actions that leave them where they are.</summary>
    Compare,
}

/// <summary>What the actions apply to: the selection in the library or the file shown in a viewer.</summary>
public sealed record ActionSelection(int FileCount, IReadOnlySet<MediaKind> Kinds, ActionPlace Place);

public enum ActionAvailability
{
    Hidden,
    Disabled,
    Enabled,
}

/// <summary>An action in a menu and whether it can run.</summary>
public readonly record struct ActionMenuItem(ActionId Id, bool IsEnabled);

/// <summary>
/// The one registry behind the context menus, the menu bar and the shortcuts (macOS ActionRegistry): order, sections,
/// shortcuts and availability by selection and features. Titles and performing live in the app.
/// </summary>
public static class ActionRegistry
{
    /// <summary>Menu sections in order (FEATURES §8); the destructive action is last and alone.</summary>
    public static IReadOnlyList<IReadOnlyList<ActionId>> Sections { get; } =
    [
        [ActionId.Open, ActionId.QuickPreview, ActionId.PlayInMiniPlayer, ActionId.Compare, ActionId.OpenWith, ActionId.EditInPrimaryApp],
        [ActionId.RotateLeft, ActionId.RotateRight, ActionId.Edit, ActionId.SplitIntoStems],
        [ActionId.AddToAlbum, ActionId.RemoveFromAlbum, ActionId.SetAlbumCover, ActionId.MoveTo, ActionId.CopyTo, ActionId.Rename],
        [ActionId.Flag],
        [ActionId.GetInfo, ActionId.ShowInExplorer, ActionId.CopyPath, ActionId.Copy],
        [ActionId.MoveToTrash],
    ];

    /// <summary>
    /// Default shortcuts (FEATURES §15 with ⌘ → Ctrl and ⌥ → Alt, WINDOWS_PORT): Explorer's own where it has one —
    /// Alt+Enter for properties, Ctrl+Shift+C for "Copy as path", Delete for the Recycle Bin, F2 to rename.
    /// </summary>
    private static readonly Dictionary<ActionId, ActionShortcut> Shortcuts = new()
    {
        [ActionId.Open] = new("Enter"),
        [ActionId.QuickPreview] = new("Space"),
        [ActionId.EditInPrimaryApp] = new("E", ActionModifiers.Control | ActionModifiers.Alt),
        [ActionId.Compare] = new("C", ActionModifiers.Control | ActionModifiers.Alt),
        [ActionId.RotateLeft] = new("L", ActionModifiers.Control),
        [ActionId.RotateRight] = new("R", ActionModifiers.Control),
        [ActionId.Edit] = new("E", ActionModifiers.Control),
        [ActionId.MoveTo] = new("M", ActionModifiers.Control | ActionModifiers.Alt),
        [ActionId.Rename] = new("F2"),
        [ActionId.GetInfo] = new("Enter", ActionModifiers.Alt),
        [ActionId.ShowInExplorer] = new("R", ActionModifiers.Control | ActionModifiers.Alt),
        [ActionId.CopyPath] = new("C", ActionModifiers.Control | ActionModifiers.Shift),
        [ActionId.Copy] = new("C", ActionModifiers.Control),
        [ActionId.MoveToTrash] = new("Delete"),
        [ActionId.RemoveFromAlbum] = new("Delete"),
    };

    public static ActionShortcut? Shortcut(ActionId id) => Shortcuts.GetValueOrDefault(id);

    public static bool IsDestructive(ActionId id) => id == ActionId.MoveToTrash;

    /// <param name="primaryAppKinds">Kinds that have a primary external app (Open With settings).</param>
    public static ActionAvailability Availability(
        ActionId id, ActionSelection selection, IReadOnlySet<ActionFeature> features, IReadOnlySet<MediaKind> primaryAppKinds)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(primaryAppKinds);
        if (selection.FileCount < 1)
        {
            return ActionAvailability.Hidden;
        }
        if (selection.Place == ActionPlace.Compare)
        {
            return id is ActionId.GetInfo or ActionId.ShowInExplorer or ActionId.CopyPath or ActionId.Copy
                ? ActionAvailability.Enabled
                : ActionAvailability.Hidden;
        }
        switch (id)
        {
            case ActionId.GetInfo or ActionId.ShowInExplorer or ActionId.CopyPath or ActionId.Copy:
                return ActionAvailability.Enabled;
            case ActionId.Flag:
                return features.Contains(ActionFeature.Flags) ? ActionAvailability.Enabled : ActionAvailability.Hidden;
            case ActionId.Compare:
                // Exactly two photos, from the library or an album.
                return selection.FileCount == 2 && selection.Place != ActionPlace.Viewer
                    && selection.Kinds.Count == 1 && selection.Kinds.Contains(MediaKind.Photo)
                    ? ActionAvailability.Enabled
                    : ActionAvailability.Hidden;
            case ActionId.PlayInMiniPlayer:
                // Songs only.
                return features.Contains(ActionFeature.MiniPlayer) && selection.Kinds.Count == 1 && selection.Kinds.Contains(MediaKind.Audio)
                    ? ActionAvailability.Enabled
                    : ActionAvailability.Hidden;
            case ActionId.SplitIntoStems:
                // Songs only; several run one after another.
                return features.Contains(ActionFeature.StemSplitting) && selection.Kinds.Count == 1 && selection.Kinds.Contains(MediaKind.Audio)
                    ? ActionAvailability.Enabled
                    : ActionAvailability.Hidden;
            case ActionId.Open or ActionId.QuickPreview:
                // In a viewer the file is already open.
                return selection.Place == ActionPlace.Viewer ? ActionAvailability.Hidden : ActionAvailability.Enabled;
            case ActionId.OpenWith or ActionId.EditInPrimaryApp:
                return OpenWithAvailability(id, selection, features, primaryAppKinds);
            case ActionId.RotateLeft or ActionId.RotateRight or ActionId.Edit:
                return PhotoAvailability(id, selection, features);
            case ActionId.AddToAlbum or ActionId.RemoveFromAlbum or ActionId.SetAlbumCover:
                return AlbumAvailability(id, selection, features);
            default:
                if (!features.Contains(ActionFeature.FileOperations))
                {
                    return ActionAvailability.Hidden;
                }
                // A viewer copies and trashes its file; moving and renaming happen in the library.
                return selection.Place == ActionPlace.Viewer && id is ActionId.MoveTo or ActionId.Rename
                    ? ActionAvailability.Hidden
                    : ActionAvailability.Enabled;
        }
    }

    private static ActionAvailability OpenWithAvailability(
        ActionId id, ActionSelection selection, IReadOnlySet<ActionFeature> features, IReadOnlySet<MediaKind> primaryAppKinds)
    {
        if (!features.Contains(ActionFeature.OpenWith))
        {
            return ActionAvailability.Hidden;
        }
        if (id != ActionId.EditInPrimaryApp)
        {
            return ActionAvailability.Enabled;
        }
        return selection.Kinds.Count == 1 && primaryAppKinds.Contains(selection.Kinds.First())
            ? ActionAvailability.Enabled
            : ActionAvailability.Hidden;
    }

    /// <summary>Adding works anywhere; removing and choosing the cover only in an album, the cover for one item.</summary>
    private static ActionAvailability AlbumAvailability(ActionId id, ActionSelection selection, IReadOnlySet<ActionFeature> features)
    {
        if (!features.Contains(ActionFeature.Albums))
        {
            return ActionAvailability.Hidden;
        }
        return id switch
        {
            ActionId.RemoveFromAlbum => selection.Place == ActionPlace.Album ? ActionAvailability.Enabled : ActionAvailability.Hidden,
            ActionId.SetAlbumCover => selection.Place != ActionPlace.Album ? ActionAvailability.Hidden
                : selection.FileCount == 1 ? ActionAvailability.Enabled : ActionAvailability.Disabled,
            _ => ActionAvailability.Enabled,
        };
    }

    /// <summary>Rotating and editing are for photos only (macOS D-41); editing takes one photo.</summary>
    private static ActionAvailability PhotoAvailability(ActionId id, ActionSelection selection, IReadOnlySet<ActionFeature> features)
    {
        if (selection.Kinds.Count != 1 || !selection.Kinds.Contains(MediaKind.Photo))
        {
            return ActionAvailability.Hidden;
        }
        if (id == ActionId.Edit)
        {
            return !features.Contains(ActionFeature.Editing) ? ActionAvailability.Hidden
                : selection.FileCount == 1 ? ActionAvailability.Enabled : ActionAvailability.Disabled;
        }
        return features.Contains(ActionFeature.Rotate) ? ActionAvailability.Enabled : ActionAvailability.Hidden;
    }

    /// <summary>The visible actions per section, empty sections dropped.</summary>
    public static IReadOnlyList<IReadOnlyList<ActionMenuItem>> Menu(
        ActionSelection selection, IReadOnlySet<ActionFeature> features, IReadOnlySet<MediaKind> primaryAppKinds)
    {
        var menu = new List<IReadOnlyList<ActionMenuItem>>();
        foreach (var section in Sections)
        {
            var items = new List<ActionMenuItem>();
            foreach (var id in section)
            {
                switch (Availability(id, selection, features, primaryAppKinds))
                {
                    case ActionAvailability.Enabled:
                        items.Add(new ActionMenuItem(id, true));
                        break;
                    case ActionAvailability.Disabled:
                        items.Add(new ActionMenuItem(id, false));
                        break;
                }
            }
            if (items.Count > 0)
            {
                menu.Add(items);
            }
        }
        return menu;
    }
}
