using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using JGlancer.App.Library;
using JGlancer.App.Shell;
using JGlancer.Core.Actions;

namespace JGlancer.App.Developer;

/// <summary>
/// <c>--file-ops-check &lt;log&gt;</c> (macOS FileOpsCheck): on the first file of the library (a temporary copy, with
/// <c>--data-dir</c>) renames it and undoes that, sends it to the Recycle Bin and undoes that, checking the file on disk
/// and the asset id each time, then writes the results and quits.
/// </summary>
internal static class FileOpsCheck
{
    public static void Attach(MainWindow window, LibraryModel model, string log)
    {
        var started = false;
        model.RefsChanged += async (_, _) =>
        {
            if (started || model.Refs.Count == 0 || window.FileOps is not { } fileOps)
            {
                return;
            }
            started = true;
            var lines = new List<string>();
            void Line(string text) => lines.Add(text);
            var id = model.Refs[0].Id;
            var path = (await model.Store.AssetsAsync([id]))[0].Path;
            var renamed = Path.Combine(Path.GetDirectoryName(path)!, "renamed by check" + Path.GetExtension(path));
            Line($"rename.problem {await fileOps.RenameAsync(path, Path.GetFileName(renamed)) ?? "none"}");
            Line($"rename.onDisk {File.Exists(renamed) && !File.Exists(path)}");
            Line($"rename.sameId {(await model.Store.LocationAsync(renamed, isDirectory: false))?.AssetId == id}");
            await fileOps.UndoAsync();
            Line($"renameUndo.onDisk {File.Exists(path) && !File.Exists(renamed)}");
            Line($"renameUndo.sameId {(await model.Store.LocationAsync(path, isDirectory: false))?.AssetId == id}");
            model.Selection.Select(id);
            await window.PerformAsync(ActionId.MoveToTrash);
            Line($"trash.gone {!File.Exists(path)}");
            Line($"trash.notInLibrary {(await model.Store.LocationAsync(path, isDirectory: false))?.AssetId is null}");
            await fileOps.UndoAsync();
            Line($"trashUndo.back {File.Exists(path)}");
            Line($"trashUndo.sameId {(await model.Store.LocationAsync(path, isDirectory: false))?.AssetId == id}");
            Line($"toast {fileOps.Toast?.Message}");
            File.WriteAllLines(log, lines);
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        };
    }
}
