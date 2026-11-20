using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using JGlancer.App.Library;
using JGlancer.App.Shell;
using JGlancer.Core.Model;

namespace JGlancer.App.Developer;

/// <summary>
/// <c>--preview-check &lt;log&gt;</c>: Quick Preview on the library's videos, its playback sampled over time: opened over
/// the pane's video, then on each next video with →. Each sample: whether it plays, the playhead and the video's size,
/// so a picture that stops while the button says it plays shows as a playhead that stands still. Logs and quits.
/// </summary>
internal static class PreviewCheck
{
    public static void Attach(MainWindow window, LibraryModel model, string log)
    {
        var started = false;
        model.RefsChanged += async (_, _) =>
        {
            var videos = model.Refs.Where(r => r.Kind == MediaKind.Video && r.Id > 0).Select(r => r.Id).ToList();
            if (started || videos.Count == 0 || window.Preview is not { } preview)
            {
                return;
            }
            started = true;
            var lines = new List<string>();
            var clock = Stopwatch.StartNew();
            async Task Sample(string step)
            {
                // The first moving frame: a picture of its size and a playhead past zero.
                var waited = Stopwatch.StartNew();
                while (preview.Playback is not { VideoSize.Width: > 0, CurrentTime: > 0 } && waited.ElapsedMilliseconds < 3000)
                {
                    await Task.Delay(2);
                }
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"{step} firstFrameMs={clock.ElapsedMilliseconds}"));
                foreach (var at in new[] { 300, 800, 1500, 2500 })
                {
                    while (clock.ElapsedMilliseconds < at)
                    {
                        await Task.Delay(10);
                    }
                    var playback = preview.Playback;
                    lines.Add(string.Create(CultureInfo.InvariantCulture,
                        $"{step} t={at} playing={playback?.IsPlaying} time={playback?.CurrentTime:0.000} mpvTime={playback?.Player.Position:0.000} size={playback?.VideoSize.Width}x{playback?.VideoSize.Height} status={playback?.Status}"));
                }
            }
            model.Selection.Select(videos[0]);
            await Task.Delay(1500);
            preview.Playback?.Player.SetLogFile(Path.ChangeExtension(log, ".mpv.log"));
            // Milestones of an opening: the surface's render context, the file opened, the picture's size known.
            var marks = new List<string>();
            void Mark(string what) => marks.Add(string.Create(CultureInfo.InvariantCulture, $"{what}@{clock.ElapsedMilliseconds}"));
            if (preview.Surface is Viewer.Media.MpvVideoView surface)
            {
                surface.RenderReady += (_, _) => Mark("renderReady");
            }
            var lastStatus = preview.Playback?.Status;
            var lastSize = preview.Playback?.VideoSize.Width ?? 0;
            preview.Changed += (_, _) =>
            {
                if (preview.Playback is { } p && p.Status != lastStatus)
                {
                    lastStatus = p.Status;
                    Mark($"status:{p.Status}");
                }
                if (preview.Playback is { } q && q.VideoSize.Width != lastSize)
                {
                    lastSize = q.VideoSize.Width;
                    Mark($"size:{lastSize}");
                }
            };
            clock.Restart();
            window.OpenQuickPreview();
            Mark("opened");
            await Sample("open");
            lines.Add("open marks " + string.Join(" ", marks));
            marks.Clear();
            // Quick Preview again on a file the pane has not loaded (the pane hidden): closed, the pane off, opened.
            window.QuickPreview?.Dismiss();
            model.SetPreviewVisible(false);
            await Task.Delay(800);
            clock.Restart();
            window.OpenQuickPreview();
            Mark("opened");
            await Sample("openPaneHidden");
            lines.Add("openPaneHidden marks " + string.Join(" ", marks));
            for (var i = 1; i < Math.Min(3, videos.Count); i++)
            {
                clock.Restart();
                var opening = false;
                void Watch(object? sender, EventArgs e) => opening |= preview.Playback?.Status == Viewer.Media.PlaybackStatus.Loading;
                preview.Changed += Watch;
                model.Selection.Select(videos[i]);
                // This file's frames, not the last one's: once its opening began.
                var waited = Stopwatch.StartNew();
                while (!opening && waited.ElapsedMilliseconds < 3000)
                {
                    await Task.Delay(2);
                }
                preview.Changed -= Watch;
                await Sample($"next{i}");
                lines.Add("next marks " + string.Join(" ", marks));
                marks.Clear();
            }
            model.SetPreviewVisible(true);
            await File.WriteAllLinesAsync(log, lines);
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        };
    }
}
