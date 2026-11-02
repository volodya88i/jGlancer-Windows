using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using JGlancer.App.Support;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Core.Playback;
using JGlancer.Design;
using JGlancer.Media.Thumbnails;

namespace JGlancer.App.Library;

/// <summary>
/// The sidebar's ALBUMS (macOS SidebarAlbums): a row per album with its cover, name and count; + makes a new one. A
/// row takes dropped tiles (they join the album), renames on double-click, and has Rename, Export to Folder… and
/// Delete Album… in its menu. Deleting an album never touches files.
/// </summary>
internal sealed partial class SidebarView
{
    private readonly Dictionary<long, Bitmap?> _covers = [];
    private readonly HashSet<long> _coversLoading = [];

    /// <summary>Copies files into a folder chosen then (set by the window: file operations).</summary>
    public Func<IReadOnlyList<string>, Task>? ExportFiles { get; set; }

    private void AttachAlbums()
    {
        NewAlbumButton.Click += async (_, _) =>
        {
            if (_model is { } model)
            {
                await model.CreateAlbumAsync();
            }
        };
    }

    private bool _rebuildingAlbums;
    private bool _albumsStale;

    /// <summary>The open name field and the album it names: a rebuild keeps what was typed (and must not commit it).</summary>
    private (long AlbumId, TextBox Field)? _albumField;

    private void RebuildAlbums()
    {
        // Removing a focused name field raises its LostFocus, which may change the albums: no rebuild inside a rebuild.
        if (_rebuildingAlbums)
        {
            _albumsStale = true;
            return;
        }
        _rebuildingAlbums = true;
        try
        {
            do
            {
                _albumsStale = false;
                RebuildAlbumRows();
            }
            while (_albumsStale);
        }
        finally
        {
            _rebuildingAlbums = false;
        }
    }

    private void RebuildAlbumRows()
    {
        var typed = _albumField is { } open && _model?.RenamingAlbumId == open.AlbumId ? (open.AlbumId, open.Field.Text, open.Field.SelectionStart, open.Field.SelectionEnd) : default;
        _albumField = null;
        AlbumRows.Children.Clear();
        if (_model is not { } model)
        {
            AlbumsHeader.IsVisible = false;
            return;
        }
        AlbumsHeader.IsVisible = model.HasRoots;
        foreach (var album in model.Albums)
        {
            AlbumRows.Children.Add(AlbumRow(model, album));
        }
        if (typed.Text is { } text && _albumField is { } field && field.AlbumId == typed.AlbumId)
        {
            field.Field.Tag = "restored";
            field.Field.Text = text;
            field.Field.SelectionStart = typed.SelectionStart;
            field.Field.SelectionEnd = typed.SelectionEnd;
        }
    }

    private Button AlbumRow(LibraryModel model, Album album)
    {
        var isSelected = Shown.State.AlbumId == album.Id;
        var renaming = model.RenamingAlbumId == album.Id;
        var cover = new Border
        {
            Width = JGMetrics.IconSize + JGSpacing.Xxs,
            Height = JGMetrics.IconSize + JGSpacing.Xxs,
            CornerRadius = new CornerRadius(JGMetrics.RadiusSmall),
            ClipToBounds = true,
            Child = CoverContent(album),
        };
        cover[!Border.BackgroundProperty] = this.GetResourceObservable(JGColor.BgRaised.ResourceKey).ToBinding();
        Control name;
        if (renaming)
        {
            var field = new TextBox { Text = album.Name, MinHeight = 0, Padding = new Thickness(JGSpacing.Xxs, 0), VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(field, L.T("Rename"));
            _albumField = (album.Id, field);
            var done = false;
            async Task Commit(bool keep)
            {
                // A field a rebuild removed is not an answer: its successor carries on.
                if (done || _rebuildingAlbums || _albumField?.Field != field)
                {
                    return;
                }
                done = true;
                if (keep)
                {
                    await model.RenameAlbumAsync(album.Id, field.Text ?? "");
                }
                else
                {
                    model.BeginAlbumRename(null);
                }
            }
            field.AddHandler(KeyDownEvent, async (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Escape)
                {
                    e.Handled = true;
                    await Commit(e.Key == Key.Enter);
                }
            }, RoutingStrategies.Tunnel);
            field.LostFocus += async (_, _) => await Commit(true);
            field.AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                field.Focus();
                // A new field selects the name; one that replaced another keeps where the typing was.
                if (field.Tag is null)
                {
                    field.SelectAll();
                }
            }, Avalonia.Threading.DispatcherPriority.Loaded);
            name = field;
        }
        else
        {
            var text = new TextBlock { Text = album.Name, Classes = { "body" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            text[!TextBlock.ForegroundProperty] = this.GetResourceObservable((isSelected ? JGColor.TextPrimary : JGColor.TextSecondary).ResourceKey).ToBinding();
            name = text;
        }
        var count = new TextBlock { Text = album.ItemCount.ToString(System.Globalization.CultureInfo.CurrentCulture), Classes = { "mono" }, VerticalAlignment = VerticalAlignment.Center };
        count[!TextBlock.ForegroundProperty] = this.GetResourceObservable(JGColor.TextTertiary.ResourceKey).ToBinding();
        var content = new Avalonia.Controls.Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = JGSpacing.Xs };
        Avalonia.Controls.Grid.SetColumn(name, 1);
        Avalonia.Controls.Grid.SetColumn(count, 2);
        content.Children.Add(cover);
        content.Children.Add(name);
        content.Children.Add(count);
        var row = new Button { Content = content, Margin = new Thickness(JGSpacing.M, 0, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        if (this.TryFindResource("JG.sidebarRow", out var theme) && theme is Avalonia.Styling.ControlTheme controlTheme)
        {
            row.Theme = controlTheme;
        }
        row.Classes.Set("selected", isSelected);
        AutomationProperties.SetName(row, album.Name);
        row.Click += (_, _) => Shown.SelectAlbum(album.Id);
        row.DoubleTapped += (_, _) => model.BeginAlbumRename(album.Id);
        row.ContextMenu = AlbumMenu(model, album);
        DragDrop.SetAllowDrop(row, true);
        row.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            // Copy: an effect the drag allows (tiles offer Copy and Move); the files themselves stay where they are.
            e.DragEffects = DroppedFiles(e).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
            row.Classes.Set("dropTarget", e.DragEffects != DragDropEffects.None);
            e.Handled = true;
        });
        row.AddHandler(DragDrop.DragLeaveEvent, (_, _) => row.Classes.Set("dropTarget", false));
        row.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            row.Classes.Set("dropTarget", false);
            e.Handled = true;
            var ids = await model.AssetIdsAsync(DroppedFiles(e));
            await model.AddToAlbumAsync(album.Id, ids);
        });
        return row;
    }

    private ContextMenu AlbumMenu(LibraryModel model, Album album)
    {
        var rename = new MenuItem { Header = L.T("Rename") };
        rename.Click += (_, _) => model.BeginAlbumRename(album.Id);
        var export = new MenuItem { Header = L.T("Export to Folder…"), IsEnabled = album.ItemCount > 0 };
        export.Click += async (_, _) =>
        {
            if (ExportFiles is { } copy)
            {
                await copy(await model.AlbumPathsAsync(album.Id));
            }
        };
        var delete = new MenuItem { Header = L.T("Delete Album…") };
        delete.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window owner
                && await ConfirmDialog.ShowAsync(owner, L.T("Delete the album “{0}”?", album.Name), L.T("The files stay where they are."), L.T("Delete Album")))
            {
                await model.DeleteAlbumAsync(album.Id);
            }
        };
        return new ContextMenu { Items = { rename, export, new Separator(), delete } };
    }

    /// <summary>The album's cover thumbnail once loaded, else the albums glyph.</summary>
    private Control CoverContent(Album album)
    {
        if (album.CoverAssetId is { } id && _covers.TryGetValue(id, out var bitmap) && bitmap is not null)
        {
            return new Support.SafeImage { Source = bitmap, Stretch = Stretch.UniformToFill };
        }
        if (album.CoverAssetId is { } wanted && !_covers.ContainsKey(wanted))
        {
            _ = LoadCoverAsync(wanted);
        }
        var glyph = new JGIcon { Data = JGIconData.Layers, Width = JGMetrics.IconSizeSmall, Height = JGMetrics.IconSizeSmall, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph[!JGIcon.ForegroundProperty] = this.GetResourceObservable(JGColor.TextTertiary.ResourceKey).ToBinding();
        return glyph;
    }

    private async Task LoadCoverAsync(long assetId)
    {
        if (_model is not { } model || !_coversLoading.Add(assetId))
        {
            return;
        }
        try
        {
            var rows = await model.Store.AssetsAsync([assetId]);
            Bitmap? bitmap = null;
            if (rows.Count > 0)
            {
                var request = Viewer.Media.MediaViewerModel.Thumbnail(PlaybackItem.From(rows[0]), ThumbnailBucket.Small);
                var image = await model.Thumbnails.GetAsync(request, ThumbnailPriority.Visible);
                bitmap = image is null ? null : await Task.Run(() => ThumbnailCache.ToBitmap(image));
            }
            _covers[assetId] = bitmap;
            RebuildAlbums();
        }
        finally
        {
            _coversLoading.Remove(assetId);
        }
    }
}
