using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using JGlancer.App.Library;
using JGlancer.App.Shell;
using JGlancer.Core.Model;

namespace JGlancer.App.Developer;

/// <summary>
/// <c>--album-check &lt;log&gt;</c> (macOS AlbumCheck): on a temporary library makes an album from the first three
/// files, shows it, removes one and undoes that, reorders, deletes the album and undoes that, logging each result;
/// the album stays (for a snapshot with <c>--open-album</c>). Then quits.
/// </summary>
internal static class AlbumCheck
{
    public static void Attach(MainWindow window, LibraryModel model, string log)
    {
        var started = false;
        model.RefsChanged += async (_, _) =>
        {
            if (started || model.Refs.Count < 3 || window.FileOps is not { } fileOps || model.State.AlbumId is not null)
            {
                return;
            }
            started = true;
            var lines = new List<string>();
            var ids = model.Refs.Take(3).Select(r => r.Id).ToList();
            var albumId = await model.CreateAlbumAsync(ids);
            // As after Ctrl+N: the items and the cover arrive while the name field is open.
            await Task.Delay(1500);
            lines.Add($"sidebar.rows {window.LibraryView.Sidebar.AlbumRows.Children.Count} albums {model.Albums.Count}");
            lines.Add($"nameField.open {model.RenamingAlbumId == albumId}");
            await model.RenameAlbumAsync(albumId, "Check");
            await Until(() => model.Albums.Any(a => a.Id == albumId && a.ItemCount == 3 && a.Name == "Check"));
            lines.Add($"create.items {model.Albums.First(a => a.Id == albumId).ItemCount}");
            model.SelectAlbum(albumId);
            await Until(() => model.Refs.Count == 3);
            lines.Add($"shown.order {string.Join(",", model.Refs.Select(r => r.Id)) == string.Join(",", ids)}");
            await model.RemoveFromCurrentAlbumAsync([ids[1]]);
            await Until(() => model.Refs.Count == 2);
            lines.Add($"remove.left {model.Refs.Count}");
            await fileOps.UndoAsync();
            await Until(() => model.Refs.Count == 3);
            lines.Add($"removeUndo.back {model.Refs.Count == 3 && model.Refs[1].Id == ids[1]}");
            await model.ReorderCurrentAlbumAsync([ids[2]], ids[0]);
            await Until(() => model.Refs.Count == 3 && model.Refs[0].Id == ids[2]);
            lines.Add($"reorder.first {model.Refs[0].Id == ids[2]}");
            await model.DeleteAlbumAsync(albumId);
            await Until(() => model.Albums.Count == 0);
            lines.Add($"delete.gone {model.Albums.Count == 0} filesKept {(await model.Store.AssetsAsync(ids)).Count == 3}");
            await fileOps.UndoAsync();
            await Until(() => model.Albums.Count == 1 && model.Albums[0].ItemCount == 3);
            var back = model.Albums.Count > 0 ? model.Albums[0] : null;
            lines.Add($"deleteUndo.back {back?.Name == "Check" && back.ItemCount == 3}");
            lines.Add($"undoTitle {fileOps.UndoTitle}");
            File.WriteAllLines(log, lines);
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        };
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 250 && !condition(); i++)
        {
            await Task.Delay(20);
        }
    }
}
