using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using JGlancer.App.Library;
using JGlancer.App.Shell;
using JGlancer.Core.Model;

namespace JGlancer.App.Developer;

/// <summary>
/// <c>--module-check &lt;log&gt;</c>: the side modules moved while running (macOS MAC-049 crashed there): a video plays in
/// the preview, the size map shows, then each module goes to the other place and back, with a selection change between
/// moves. Each step logs where the modules are, whether the video still plays and its playhead. Logs and quits.
/// </summary>
internal static class ModuleCheck
{
    public static void Attach(MainWindow window, LibraryModel model, string log)
    {
        var started = false;
        model.RefsChanged += async (_, _) =>
        {
            var videos = model.Refs.Where(r => r.Kind == MediaKind.Video && r.Id > 0).Select(r => r.Id).ToList();
            var others = model.Refs.Where(r => r.Kind == MediaKind.Photo && r.Id > 0).Select(r => r.Id).ToList();
            if (started || videos.Count == 0 || others.Count == 0 || window.Preview is not { } preview)
            {
                return;
            }
            started = true;
            var lines = new List<string>();
            try
            {
                async Task Step(string what, Action change)
                {
                    change();
                    await Task.Delay(900);
                    var p = preview.Playback;
                    lines.Add(string.Create(CultureInfo.InvariantCulture,
                        $"{what}: preview={model.State.SlotOf(LibraryModule.Preview)?.ToString() ?? "hidden"} map={model.State.SlotOf(LibraryModule.SizeMap)?.ToString() ?? "hidden"} playing={p?.IsPlaying} time={p?.CurrentTime:0.00} size={p?.VideoSize.Width}x{p?.VideoSize.Height}"));
                }
                await Step("video", () => model.Selection.Select(videos[0]));
                await Step("play", () => preview.Playback?.Play());
                await Step("show map", () => model.SetModuleVisible(LibraryModule.SizeMap, true));
                await Step("move map", () => model.MoveModule(LibraryModule.SizeMap));
                await Step("select photo", () => model.Selection.Select(others[0]));
                await Step("select video", () => model.Selection.Select(videos[0]));
                await Step("play again", () => preview.Playback?.Play());
                await Step("move preview", () => model.MoveModule(LibraryModule.Preview));
                await Step("hide map", () => model.SetModuleVisible(LibraryModule.SizeMap, false));
                await Step("move preview back", () => model.MoveModule(LibraryModule.Preview));
                lines.Add("ok");
            }
            catch (Exception exception)
            {
                lines.Add("failed: " + exception);
            }
            await File.WriteAllLinesAsync(log, lines);
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        };
    }
}
