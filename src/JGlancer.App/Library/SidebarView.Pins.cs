using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using JGlancer.Core.Localization;
using JGlancer.Design;

namespace JGlancer.App.Library;

/// <summary>
/// The PINNED section (W-76): a row per pinned folder with a pin and its name (its path as the tooltip); a click shows
/// it in the active pane and opens its parents in the tree; the right-click menu unpins it or shows it in Explorer.
/// Folder rows pin and unpin through their own menu.
/// </summary>
internal sealed partial class SidebarView
{
    private void RebuildPins()
    {
        PinnedRows.Children.Clear();
        if (_model is not { } model)
        {
            PinnedSection.IsVisible = false;
            return;
        }
        var shown = Shown;
        var selected = shown.State.AlbumId is null ? shown.State.FolderId : null;
        foreach (var id in model.PinnedFolders)
        {
            // A pinned folder gone from disk (or from the library) is skipped, not forgotten: it may come back.
            if (model.Folder(id) is { } folder && model.Roots.FirstOrDefault(r => r.Id == folder.RootId) is { } root)
            {
                var path = Core.Store.LibraryPaths.Absolute(root.Path, folder.RelativePath);
                PinnedRows.Children.Add(PinRow(id, folder.Name, path, id == selected));
            }
        }
        PinnedSection.IsVisible = PinnedRows.Children.Count > 0;
    }

    private Button PinRow(long folderId, string name, string path, bool isSelected)
    {
        var pin = new JGIcon { Data = JGCustomIcons.Pin, VerticalAlignment = VerticalAlignment.Center };
        pin[!JGIcon.ForegroundProperty] = this.GetResourceObservable(JGColor.TextTertiary.ResourceKey).ToBinding();
        var text = new TextBlock { Text = name, Classes = { "body" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        text[!TextBlock.ForegroundProperty] = this.GetResourceObservable((isSelected ? JGColor.TextPrimary : JGColor.TextSecondary).ResourceKey).ToBinding();
        var row = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = JGSpacing.Xs, Children = { pin, text } },
            // In line with the album rows below.
            Margin = new Thickness(JGSpacing.M, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        if (this.TryFindResource("JG.sidebarRow", out var theme) && theme is Avalonia.Styling.ControlTheme controlTheme)
        {
            row.Theme = controlTheme;
        }
        row.Classes.Set("selected", isSelected);
        AutomationProperties.SetName(row, name);
        ToolTip.SetTip(row, path);
        row.Click += (_, _) => Shown.RevealFolder(folderId);
        var unpin = new MenuItem { Header = L.T("Unpin") };
        unpin.Click += (_, _) => _model?.TogglePin(folderId);
        var show = new MenuItem { Header = L.T("Show in Explorer") };
        show.Click += (_, _) => Platform.Windows.ShellFiles.ShowInExplorer([path]);
        row.ContextMenu = new ContextMenu { Items = { show, unpin } };
        return row;
    }

    private void OnTogglePin(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: SidebarRow row })
        {
            _model?.TogglePin(row.Id);
        }
    }
}
