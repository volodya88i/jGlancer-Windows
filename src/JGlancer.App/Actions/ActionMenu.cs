using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using JGlancer.App.Library;
using JGlancer.Core.Actions;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Design;
using JGlancer.Platform.Windows;

namespace JGlancer.App.Actions;

/// <summary>
/// The context menu (macOS ActionMenu, FEATURES §8): built from the registry, shortcut hints on the right, Open With
/// as a submenu with program icons.
/// </summary>
internal static class ActionMenu
{
    /// <summary>The menu for <paramref name="target"/>, or null when it has nothing.</summary>
    public static ContextMenu? Make(ActionTarget target, ActionPerformer performer, double scaling)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(performer);
        var sections = performer.Menu(target);
        if (sections.Count == 0)
        {
            return null;
        }
        var menu = new ContextMenu();
        var entries = new List<OpenWithEntry>();
        var icons = new List<IDisposable>();
        foreach (var section in sections)
        {
            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }
            foreach (var action in section)
            {
                var item = new MenuItem { Header = performer.Title(action.Id, target), IsEnabled = action.IsEnabled };
                // In an album Delete takes files out of it: the Recycle Bin shows no key there.
                if (Settings.SettingsStore.Current.Shortcut(action.Id) is { } shortcut && !(action.Id == ActionId.MoveToTrash && target.Place == ActionPlace.Album))
                {
                    item.InputGesture = Gesture(shortcut);
                }
                if (action.Id == ActionId.AddToAlbum)
                {
                    AddAlbums(item, target, performer);
                }
                else if (action.Id == ActionId.Flag)
                {
                    AddFlags(item, target, performer);
                }
                else if (action.Id is ActionId.MoveTo or ActionId.CopyTo)
                {
                    AddFolders(item, target, performer, copying: action.Id == ActionId.CopyTo);
                }
                else if (action.Id == ActionId.OpenWith)
                {
                    var built = performer.OpenWith.Entries(target.Kind, target.Sample);
                    entries.AddRange(built);
                    AddOpenWith(item, built, target, performer, scaling, icons);
                }
                else
                {
                    var id = action.Id;
                    item.Click += async (_, _) => await performer.PerformAsync(id, target);
                }
                menu.Items.Add(item);
            }
        }
        // The suggested programs' shell objects go with the menu.
        menu.Closed += (_, _) =>
        {
            OpenWithStore.Release(entries);
            icons.ForEach(icon => icon.Dispose());
        };
        return menu;
    }

    /// <summary>The menu hint of <paramref name="shortcut"/> (null when Avalonia cannot spell it).</summary>
    public static KeyGesture? Gesture(ActionShortcut? shortcut)
    {
        if (shortcut is null)
        {
            return null;
        }
        try
        {
            return KeyGesture.Parse(shortcut.Gesture);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The accent, the six colors, Remove Flag: colored stars with their keys (macOS ActionMenu).</summary>
    public static void AddFlags(MenuItem parent, ActionTarget target, ActionPerformer performer)
    {
        ArgumentNullException.ThrowIfNull(parent);
        foreach (var flag in FlagStyle.MenuOrder)
        {
            if (flag == AssetFlag.None)
            {
                parent.Items.Add(new Separator());
            }
            var item = new MenuItem
            {
                Header = FlagStyle.Title(flag),
                Icon = FlagStyle.Glyph(parent, flag, JGMetrics.IconSize),
                InputGesture = Gesture(Settings.SettingsStore.Current.Shortcuts.Shortcut(ShortcutCommands.CommandFor(flag))),
            };
            item.Click += async (_, _) => await performer.SetFlagAsync(target, flag);
            parent.Items.Add(item);
        }
    }

    /// <summary>The albums (not the one shown), marked when they already hold all or some of the files, then New Album….</summary>
    private static void AddAlbums(MenuItem parent, ActionTarget target, ActionPerformer performer)
    {
        foreach (var (album, contained) in performer.AlbumsForAdding(target))
        {
            var title = contained == 0 ? album.Name
                : contained >= target.Count ? L.T("{0} (already added)", album.Name)
                : L.T("{0} (partly added)", album.Name);
            var item = new MenuItem { Header = title };
            item.Click += async (_, _) => await performer.AddToAlbumAsync(target, album.Id);
            parent.Items.Add(item);
        }
        if (parent.Items.Count > 0)
        {
            parent.Items.Add(new Separator());
        }
        var create = new MenuItem { Header = L.T("New Album…") };
        create.Click += async (_, _) => await performer.AddToNewAlbumAsync(target);
        parent.Items.Add(create);
    }

    /// <summary>Recent destination folders (full path as the tooltip), then Choose Folder….</summary>
    private static void AddFolders(MenuItem parent, ActionTarget target, ActionPerformer performer, bool copying)
    {
        foreach (var folder in performer.RecentFolders)
        {
            var item = new MenuItem { Header = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : folder };
            ToolTip.SetTip(item, folder);
            item.Click += async (_, _) => await performer.TransferAsync(target, folder, copying);
            parent.Items.Add(item);
        }
        if (parent.Items.Count > 0)
        {
            parent.Items.Add(new Separator());
        }
        var choose = new MenuItem { Header = L.T("Choose Folder…") };
        choose.Click += async (_, _) => await performer.TransferAsync(target, null, copying);
        parent.Items.Add(choose);
    }

    private static void AddOpenWith(MenuItem parent, IReadOnlyList<OpenWithEntry> entries, ActionTarget target, ActionPerformer performer, double scaling, List<IDisposable> icons)
    {
        foreach (var entry in entries)
        {
            switch (entry)
            {
                case OpenWithEntry.Separator:
                    parent.Items.Add(new Separator());
                    break;
                case OpenWithEntry.Configured configured:
                    parent.Items.Add(Item(
                        configured.IsInstalled ? configured.App.Name : L.T("{0} (not installed)", configured.App.Name),
                        configured.IsInstalled ? configured.App.Path : null, configured.IsInstalled, entry, target, performer, scaling, icons));
                    break;
                case OpenWithEntry.Suggested suggested:
                    var handler = suggested.Handler;
                    parent.Items.Add(Item(
                        handler.IsDefault ? L.T("{0} (default)", handler.DisplayName) : handler.DisplayName,
                        File.Exists(handler.Name) ? handler.Name : handler.IconPath, true, entry, target, performer, scaling, icons));
                    break;
                case OpenWithEntry.Other:
                    parent.Items.Add(Item(L.T("Other…"), null, true, entry, target, performer, scaling, icons));
                    break;
            }
        }
    }

    private static MenuItem Item(string title, string? iconPath, bool isEnabled, OpenWithEntry entry, ActionTarget target, ActionPerformer performer, double scaling, List<IDisposable> icons)
    {
        var item = new MenuItem { Header = title, IsEnabled = isEnabled };
        if (iconPath is not null && iconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && ShellOpenWith.Icon(iconPath, (int)Math.Ceiling(JGMetrics.IconSize * scaling)) is { } icon)
        {
            var bitmap = ThumbnailCache.ToBitmap(icon);
            icons.Add(bitmap);
            item.Icon = new Image
            {
                Source = bitmap,
                Width = JGMetrics.IconSize,
                Height = JGMetrics.IconSize,
            };
        }
        // Programs without an icon keep its place, so every name in the list lines up.
        item.Icon ??= new Panel { Width = JGMetrics.IconSize, Height = JGMetrics.IconSize };
        item.Click += async (_, _) => await performer.OpenAsync(target, entry);
        return item;
    }
}
