using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using JGlancer.App.Library;
using JGlancer.App.Shell;
using JGlancer.Core.Model;

namespace JGlancer.App.Developer;

/// <summary>
/// <c>--flag-check &lt;log&gt;</c> (macOS D-64 end-to-end check): on a temporary library flags two files red, shows
/// only red ones, presses red again (the flag comes off), undoes that, searches by name in the folder and Everywhere,
/// logging each result. The red flags stay (for a snapshot with <c>--flag-first</c> or the filter). Then quits.
/// </summary>
internal static class FlagCheck
{
    public static void Attach(MainWindow window, LibraryModel model, string log)
    {
        var started = false;
        model.RefsChanged += async (_, _) =>
        {
            if (started || model.Refs.Count < 3 || window.FileOps is not { } fileOps)
            {
                return;
            }
            started = true;
            var lines = new List<string>();
            var all = model.Refs.Count;
            var ids = model.Refs.Take(2).Select(r => r.Id).ToList();
            await model.SetFlagAsync(AssetFlag.Red, ids);
            model.SetFlagFilter(new FlagFilter(AssetFlag.Red));
            await Until(() => model.Refs.Count == 2);
            lines.Add($"red.shown {model.Refs.Count}");
            await model.SetFlagAsync(AssetFlag.Red, ids);
            await Until(() => model.Refs.Count == 0);
            lines.Add($"pressAgain.shown {model.Refs.Count}");
            await fileOps.UndoAsync();
            await Until(() => model.Refs.Count == 2);
            lines.Add($"undo.shown {model.Refs.Count} title {fileOps.RedoTitle}");
            model.SetFlagFilter(FlagFilter.Flagged);
            await model.SetFlagAsync(AssetFlag.Accent, [model.Refs[0].Id]);
            await Until(() => model.Refs.Count == 2);
            lines.Add($"flagged.shown {model.Refs.Count} accent {(await model.FlagOfAsync(ids[0])) == AssetFlag.Accent}");
            // Indexing may still add files: the shown count without filters is the baseline from here on.
            model.SetFlagFilter(null);
            await Until(() => model.Refs.Count >= all);
            all = model.Refs.Count;
            var name = (await model.Store.AssetsAsync([ids[1]]))[0].Name;
            model.SetSearch(name[..Math.Min(name.Length, 6)].ToUpperInvariant());
            await Until(() => model.Refs.Count < all && model.Refs.Count > 0);
            lines.Add($"search.shown {model.Refs.Count} has {model.Refs.Any(r => r.Id == ids[1])}");
            model.SetSearch("deep");
            await Until(() => model.Refs.Count == 0 || model.Refs.Count == 1);
            var inFolder = model.Refs.Count;
            model.SetSearchEverywhere(true);
            await Until(() => model.Refs.Count == 1);
            lines.Add($"deep.folder {inFolder} everywhere {model.Refs.Count}");
            model.EndSearch();
            await Until(() => model.Refs.Count == all);
            lines.Add($"endSearch.shown {model.Refs.Count == all}");
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
