using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace JGlancer.App.Library;

/// <summary>
/// Tiles dragged out of the grid (W-48): the selected files, as Explorer drags them — onto a sidebar folder (moved or
/// copied through the file operations, undoable) or into Explorer and other programs.
/// </summary>
internal sealed partial class BrowserPane
{
    private bool _isDragging;

    /// <summary>Tiles are being dragged out of a pane: a drop back on the window is not "open these files".</summary>
    public static bool IsDraggingOut { get; private set; }

    private void AttachAlbumReorder()
    {
        DragDrop.SetAllowDrop(TileGrid, true);
        TileGrid.AddHandler(DragDrop.DragOverEvent, OnGridDragOver);
        TileGrid.AddHandler(DragDrop.DragLeaveEvent, (_, _) => TileGrid.DropBar = null);
        TileGrid.AddHandler(DragDrop.DropEvent, OnGridDrop);
    }

    /// <summary>
    /// Where tiles dragged within an album land (macOS GridController+AlbumOrder): in front of the tile under the
    /// pointer (behind it on its right half), or at the end; null outside an album or for drags from elsewhere.
    /// </summary>
    private (long? Before, Core.Model.GridRect Bar)? AlbumDropTarget(DragEventArgs e)
    {
        if (!_isDragging || Model is not { State.AlbumId: not null } model || model.Refs.Count == 0)
        {
            return null;
        }
        var geometry = TileGrid.Geometry;
        var point = e.GetPosition(TileGrid);
        var y = point.Y + TileGrid.Offset.Y;
        var bar = Design.JGMetrics.SelectionEdgeWidth;
        if (geometry.IndexAt(point.X, y) is not { } index || index >= model.Refs.Count)
        {
            var last = geometry.Frame(model.Refs.Count - 1);
            return (null, new Core.Model.GridRect(last.Right + geometry.Gutter / 2 - bar / 2, last.Y, bar, geometry.TileSide));
        }
        var frame = geometry.Frame(index);
        var after = point.X > frame.X + frame.Width / 2;
        var landing = after ? index + 1 : index;
        var x = after ? frame.Right + geometry.Gutter / 2 : frame.X - geometry.Gutter / 2;
        long? before = landing < model.Refs.Count ? model.Refs[landing].Id : null;
        return (before, new Core.Model.GridRect(x - bar / 2, frame.Y, bar, geometry.TileSide));
    }

    private void OnGridDragOver(object? sender, DragEventArgs e)
    {
        if (AlbumDropTarget(e) is not { } target)
        {
            TileGrid.DropBar = null;
            return;
        }
        TileGrid.DropBar = target.Bar;
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private async void OnGridDrop(object? sender, DragEventArgs e)
    {
        TileGrid.DropBar = null;
        if (AlbumDropTarget(e) is not { } target || Model is not { } model)
        {
            return;
        }
        e.Handled = true;
        var moving = model.Selection.Selected
            .Select(id => (id, index: model.Order.IndexOf(id)))
            .Where(p => p.index is not null)
            .OrderBy(p => p.index)
            .Select(p => p.id)
            .ToList();
        if (moving.Count > 0 && (target.Before is not { } before || !moving.Contains(before)))
        {
            await model.ReorderCurrentAlbumAsync(moving, target.Before);
        }
    }

    private async void OnGridDragRequested(object? sender, PointerPressedEventArgs press)
    {
        if (_isDragging || Model is not { } model || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }
        _isDragging = true;
        IsDraggingOut = true;
        try
        {
            var ids = model.Selection.Selected
                .Select(id => (id, index: model.Order.IndexOf(id)))
                .Where(p => p.index is not null)
                .OrderBy(p => p.index)
                .Select(p => p.id)
                .ToList();
            // A folded image sequence drags all of its frames (W-81).
            var assets = await model.Store.AssetsAsync(model.WithSequenceFrames(ids));
            var data = new DataTransfer();
            foreach (var asset in assets)
            {
                if (await storage.TryGetFileFromPathAsync(new Uri(asset.Path)) is { } file)
                {
                    data.Add(DataTransferItem.CreateFile(file));
                }
            }
            if (data.Items.Count > 0)
            {
                await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Copy | DragDropEffects.Move);
            }
        }
        finally
        {
            _isDragging = false;
            IsDraggingOut = false;
        }
    }
}
