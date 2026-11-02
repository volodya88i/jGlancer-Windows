using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JGlancer.App.Library;
using JGlancer.App.Shell;
using JGlancer.Core.Model;

namespace JGlancer.App.Developer;

/// <summary>Grid presets, the "settled" test for snapshots and the library benchmark (macOS -JG… hooks, D-39).</summary>
internal static class DeveloperHooks
{
    private const double ScrollSpeed = 4000;
    private static readonly TimeSpan ScrollDuration = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(30);

    public static TimeSpan SinceProcessStart => DateTime.Now - Process.GetCurrentProcess().StartTime;

    /// <summary><c>--open-first</c>: opens the viewer on the first photo of the first list shown.</summary>
    public static void Attach(MainWindow window, LibraryModel model, DeveloperOptions options)
    {
        if (options.QuickPreview)
        {
            // --quick-preview: once the selection is there, as if Space were pressed.
            var shown = false;
            model.Selection.Changed += (_, _) =>
            {
                if (!shown && model.Selection.Previewed is not null)
                {
                    shown = true;
                    Dispatcher.UIThread.Post(window.OpenQuickPreview, DispatcherPriority.Background);
                }
            };
        }
        if (options.StemCheck is { } stemLog)
        {
            // `--stem-check <log>`: the first song split into 4 stems, without the window's questions.
            var started = false;
            model.RefsChanged += async (_, _) =>
            {
                if (!started && model.Refs.FirstOrDefault(r => r.Kind == Core.Model.MediaKind.Audio && r.Id > 0) is { } song)
                {
                    started = true;
                    // While the folder's first scan runs its path may not be known yet: the next change tries again.
                    if (await model.Store.AssetsAsync([song.Id]) is [var row])
                    {
                        await window.RunStemCheckAsync(row.Path, stemLog);
                    }
                    else
                    {
                        started = false;
                    }
                }
            };
        }
        if (options.CompareFirst)
        {
            // `--compare-first`: the first two photos side by side.
            var compared = false;
            model.RefsChanged += async (_, _) =>
            {
                var photos = model.Refs.Where(r => r.Kind == Core.Model.MediaKind.Photo && r.Id > 0).Take(2).Select(r => r.Id).ToList();
                if (!compared && photos.Count == 2)
                {
                    compared = true;
                    await window.CompareAsync(photos);
                }
            };
        }
        if (!options.OpenFirst)
        {
            return;
        }
        var opened = false;
        model.RefsChanged += async (_, _) =>
        {
            if (!opened && model.Refs.FirstOrDefault(r => r.Kind == options.OpenKind) is { Id: > 0 } photo)
            {
                opened = true;
                await window.OpenAsync(photo.Id);
                window.Viewer?.Model?.Move(options.ViewerStep);
                if (window.MediaViewer?.Model is { } media && options.ViewerStep != 0)
                {
                    media.MoveTo(media.Index + options.ViewerStep);
                }
                if (window.MediaViewer?.Model is { } selecting && options.Segment)
                {
                    await Until(() => selecting.Playback.Status == Viewer.Media.PlaybackStatus.Ready && selecting.Playback.Duration > 0);
                    selecting.ToggleSegmentMode();
                }
                if (options.ShowFilmstrip)
                {
                    window.Viewer?.PinFilmstrip();
                }
                if (window.MediaViewer is { Model: { } video } viewer && options.SaveFrame)
                {
                    // `--save-frame`: once the first frame is up, it goes next to the video (a temp copy).
                    await Until(() => video.Playback.VideoSize.Width > 0);
                    await Task.Delay(300);
                    await viewer.SaveFrameAsync(copy: false);
                }
            }
        };
    }

    /// <summary>Applies <c>--tile-side</c> and <c>--select-first</c> before the window shows.</summary>
    public static void Prepare(LibraryModel model, DeveloperOptions options)
    {
        if (options.UnfoldFirst)
        {
            // `--unfold-first`: the first image sequence unfolded.
            var unfolded = false;
            model.RefsChanged += (_, _) =>
            {
                // AssetRef is a struct: FirstOrDefault of nothing is a ref with id 0, not null.
                if (!unfolded && model.Refs.FirstOrDefault(r => model.IsFolded(r.Id)) is { Id: > 0 } stack)
                {
                    unfolded = true;
                    Dispatcher.UIThread.Post(() => model.ToggleSequence(stack.Id));
                }
            };
        }
        if (options.PinFirst)
        {
            // `--pin-first`: the first subfolder of the first library folder, pinned once the tree is there.
            model.TreesChanged += (_, _) =>
            {
                if (model.PinnedFolders.Count == 0 && model.Trees.Values.FirstOrDefault()?.Children is [var first, ..])
                {
                    model.TogglePin(first.Id);
                }
            };
        }
        if (options.TileSide is { } side)
        {
            model.SetTileSide(side);
        }
        if (options.SelectFirst)
        {
            // Every time the shown list arrives without a selection: the first query may be replaced by the
            // first folder's once its tree loads, which clears the selection.
            model.RefsChanged += (_, _) =>
            {
                if (model.Selection.IsEmpty && model.Refs.FirstOrDefault(r => options.SelectKind is not { } kind || r.Kind == kind) is { Id: > 0 } first)
                {
                    model.Selection.Select(first.Id);
                    if (options.FlagFirst)
                    {
                        _ = model.Store.SetFlagAsync(AssetFlag.Red, [first.Id]);
                    }
                }
            };
        }
        if (options.Search is { } text)
        {
            var searched = false;
            model.RefsChanged += (_, _) =>
            {
                if (!searched && model.Refs.Count > 0)
                {
                    searched = true;
                    model.SetSearch(text);
                }
            };
        }
    }

    /// <summary>
    /// True when the window shows what a user would see after waiting: the library index loaded, every visible thumbnail
    /// in place and (with <paramref name="includeIndexing"/>) background indexing finished, so durations show.
    /// </summary>
    public static bool IsSettled(MainWindow window, bool includeIndexing = true)
    {
        if (window.DataContext is not LibraryModel model)
        {
            return true;
        }
        if (window.Viewer?.Model is { } viewer)
        {
            return viewer.Failed || viewer.Picture is { IsPlaceholder: false };
        }
        if (window.MediaViewer?.Model is { } media)
        {
            return media.Playback.Status is Viewer.Media.PlaybackStatus.Ready or Viewer.Media.PlaybackStatus.Failed;
        }
        if (window.Preview is { } preview && (!preview.IsComplete || preview.Playback is { Status: Viewer.Media.PlaybackStatus.Loading } && preview.OwnsPlayback))
        {
            return false;
        }
        var grid = window.LibraryView.LibraryGrid;
        return !model.HasRoots
            || (model.IsLoaded && (!includeIndexing || model.Indexer.Activity == LibraryActivity.None) && grid.PendingRows == 0 && grid.PendingThumbnails == 0);
    }

    /// <summary>
    /// Polls <see cref="IsSettled"/> a few times a second (developer runs only), then calls <paramref name="done"/>
    /// with false if it gave up after <see cref="SettleTimeout"/>.
    /// </summary>
    public static void WhenSettled(MainWindow window, Action<bool> done, bool includeIndexing = true)
    {
        var started = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        var calm = 0;
        timer.Tick += (_, _) =>
        {
            // Two calm ticks in a row: a thumbnail that arrives may trigger the next request.
            calm = IsSettled(window, includeIndexing) ? calm + 1 : 0;
            if (calm >= 2 || started.Elapsed > SettleTimeout)
            {
                timer.Stop();
                done(calm >= 2);
            }
        };
        timer.Start();
    }

    /// <summary>What <see cref="IsSettled"/> waits for, as "key value unit" lines (when it gave up).</summary>
    private static void DescribeUnsettled(MainWindow window, Action<string, double, string> line)
    {
        if (window.DataContext is LibraryModel model)
        {
            var grid = window.LibraryView.LibraryGrid;
            line("settle.timedOut", 1, "");
            line("settle.loaded", model.IsLoaded ? 1 : 0, "");
            line("settle.activity", (int)model.Indexer.Activity, "");
            line("settle.pendingRows", grid.PendingRows, "rows");
            line("settle.pendingThumbnails", grid.PendingThumbnails, "thumbnails");
        }
    }

    /// <summary>
    /// <c>--benchmark</c>: time to the index on screen and the first thumbnail, time until the first screen is
    /// complete, then <see cref="ScrollDuration"/> of scrolling at <see cref="ScrollSpeed"/> px/s counting late frames,
    /// and memory. Writes "key value unit" lines and quits.
    /// </summary>
    public static void Benchmark(MainWindow window, string path)
    {
        if (window.DataContext is not LibraryModel model)
        {
            return;
        }
        var results = new StringBuilder();
        void Line(string key, double value, string unit) =>
            results.Append(string.Create(CultureInfo.InvariantCulture, $"{key} {value:0.##} {unit}{Environment.NewLine}"));

        var grid = window.LibraryView.LibraryGrid;
        var indexShown = false;
        model.RefsChanged += (_, _) =>
        {
            if (!indexShown && model.Refs.Count > 0)
            {
                indexShown = true;
                Line("library.indexShown", SinceProcessStart.TotalMilliseconds, "ms");
                Line("library.items", model.Refs.Count, "items");
            }
        };
        var thumbnailShown = false;
        grid.FirstThumbnailShown += (_, _) =>
        {
            if (!thumbnailShown)
            {
                thumbnailShown = true;
                Line("library.firstThumbnail", SinceProcessStart.TotalMilliseconds, "ms");
            }
        };
        window.Opened += (_, _) => WhenSettled(window, async settled =>
        {
            Line("library.firstScreenComplete", SinceProcessStart.TotalMilliseconds, "ms");
            if (!settled)
            {
                DescribeUnsettled(window, Line);
            }
            await Flick(window, model, Line);
            Scroll(window, grid, Line, async () => { await JumpsAndFilters(window, model, Line); ViewerSteps(window, model, Line, () =>
            {
                using var process = Process.GetCurrentProcess();
                Line("memory.workingSet", process.WorkingSet64 / 1048576.0, "MB");
                Line("memory.managed", GC.GetTotalMemory(false) / 1048576.0, "MB");
                var gc = GC.GetGCMemoryInfo();
                Line("memory.gcCommitted", gc.TotalCommittedBytes / 1048576.0, "MB");
                Line("memory.private", process.PrivateMemorySize64 / 1048576.0, "MB");
                Line("memory.bitmapCache", model.Services.Bitmaps.Bytes / 1048576.0, "MB");
                File.AppendAllText(path, results.ToString());
                (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
            }); });
        }, includeIndexing: false);
    }

    /// <summary>
    /// The viewer part of the benchmark: opens the first photo (time until its screen-size picture shows), then
    /// pages forward <see cref="ViewerSteps"/> times a quarter second apart, as a user holding → would, and reports
    /// how long each page took to show (budget 50 ms: neighbors are decoded ahead).
    /// </summary>
    private static async void ViewerSteps(MainWindow window, LibraryModel model, Action<string, double, string> line, Action done)
    {
        const int Steps = 20;
        if (model.Refs.FirstOrDefault(r => r.Kind == Core.Model.MediaKind.Photo) is not { Id: > 0 } photo)
        {
            done();
            return;
        }
        var started = Stopwatch.StartNew();
        await window.OpenAsync(photo.Id);
        if (window.Viewer?.Model is not { } viewer)
        {
            done();
            return;
        }
        // Something on the stage (the grid's thumbnail standing in), then the decoded photo.
        await Until(() => viewer.Failed || viewer.Picture is not null);
        line("viewer.firstPicture", started.Elapsed.TotalMilliseconds, "ms");
        await Until(() => viewer.Failed || viewer.Picture is { IsPlaceholder: false });
        line("viewer.firstPhoto", started.Elapsed.TotalMilliseconds, "ms");
        var times = new List<double>();
        for (var step = 0; step < Steps && viewer.CanMove(1); step++)
        {
            await Task.Delay(250);
            viewer.Move(1);
            await Until(() => viewer.Failed || viewer.Picture is { IsPlaceholder: false });
            if (viewer.LastNavigationMilliseconds is { } milliseconds)
            {
                times.Add(milliseconds);
            }
        }
        if (times.Count > 0)
        {
            times.Sort();
            line("viewer.pages", times.Count, "pages");
            line("viewer.nextMedian", times[times.Count / 2], "ms");
            line("viewer.nextWorst", times[^1], "ms");
        }
        using (var process = Process.GetCurrentProcess())
        {
            line("memory.viewerWorkingSet", process.WorkingSet64 / 1048576.0, "MB");
            line("memory.viewerManaged", GC.GetTotalMemory(false) / 1048576.0, "MB");
        }
        await HeldArrow(viewer, line);
        window.CloseViewer();
        await MediaSteps(window, model, line);
        done();
    }

    /// <summary>
    /// A flick: from the top, three screens a second for 1.5 s (much faster than thumbnails can be made), then a stop.
    /// Time from the stop until every visible tile is complete, the generations started meanwhile, and how many of
    /// them were for tiles that are not on screen at the end (work spent on tiles that only flew by).
    /// </summary>
    private static async Task Flick(MainWindow window, LibraryModel model, Action<string, double, string> line)
    {
        var grid = window.LibraryView.LibraryGrid;
        grid.Offset = default;
        grid.RaiseScrollInvalidated(EventArgs.Empty);
        await Task.Delay(1500);
        var before = model.Thumbnails.Statistics;
        var generatedBefore = before.Photos + before.Videos + before.Audio;
        var speed = grid.Viewport.Height * 3;
        var clock = Stopwatch.StartNew();
        var last = clock.Elapsed;
        while (clock.Elapsed < TimeSpan.FromSeconds(1.5))
        {
            await Task.Delay(16);
            var now = clock.Elapsed;
            grid.Offset = new Vector(0, grid.Offset.Y + speed * (now - last).TotalSeconds);
            grid.RaiseScrollInvalidated(EventArgs.Empty);
            last = now;
        }
        var stopped = Stopwatch.StartNew();
        while (!grid.IsVisibleComplete && stopped.Elapsed < TimeSpan.FromSeconds(60))
        {
            await Task.Delay(10);
        }
        line("flick.screenComplete", stopped.Elapsed.TotalMilliseconds, "ms");
        var after = model.Thumbnails.Statistics;
        line("flick.generated", after.Photos + after.Videos + after.Audio - generatedBefore, "");
        // Let what is still queued settle, then count once more.
        await Task.Delay(3000);
        var settled = model.Thumbnails.Statistics;
        line("flick.generatedIn3s", settled.Photos + settled.Videos + settled.Audio - generatedBefore, "");
    }

    /// <summary>
    /// Real-use library steps: six jumps to scattered places (like dragging the scroll bar) and four kind-filter
    /// toggles, each timed until every visible tile shows its thumbnail; CPU share over all of it, the peak working
    /// set, the grid's average frame cost, and what the thumbnail service generated per kind.
    /// </summary>
    private static async Task JumpsAndFilters(MainWindow window, LibraryModel model, Action<string, double, string> line)
    {
        var grid = window.LibraryView.LibraryGrid;
        using var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();
        long peak = 0;
        var (framesBefore, renderBefore) = grid.RenderStatistics;

        async Task<double> UntilVisible()
        {
            var started = Stopwatch.StartNew();
            await Task.Delay(50);
            var logged = false;
            while (!grid.IsVisibleComplete && started.Elapsed < TimeSpan.FromSeconds(60))
            {
                if (!logged && started.Elapsed > TimeSpan.FromSeconds(3))
                {
                    logged = true;
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "jGlancer-slow-steps.txt"),
                        $"{DateTime.Now:HH:mm:ss} at 3 s: {grid.PrefetchState}{Environment.NewLine}");
                }
                await Task.Delay(20);
                process.Refresh();
                peak = Math.Max(peak, process.WorkingSet64);
            }
            return started.Elapsed.TotalMilliseconds;
        }

        var jumps = new List<double>();
        foreach (var fraction in new[] { 0.83, 0.17, 0.55, 0.31, 0.97, 0.62 })
        {
            var maxOffset = Math.Max(0, grid.Extent.Height - grid.Viewport.Height);
            grid.Offset = new Vector(0, maxOffset * fraction);
            grid.RaiseScrollInvalidated(EventArgs.Empty);
            jumps.Add(await UntilVisible());
        }
        var filters = new List<double>();
        foreach (var kind in new[] { Core.Model.MediaKind.Video, Core.Model.MediaKind.Video, Core.Model.MediaKind.Audio, Core.Model.MediaKind.Audio })
        {
            model.ToggleKind(kind);
            var took = await UntilVisible();
            filters.Add(took);
            if (took > 2000)
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "jGlancer-slow-steps.txt"),
                    $"{DateTime.Now:HH:mm:ss} filter {kind} {took:0} ms: {grid.PrefetchState}{Environment.NewLine}");
            }
        }
        process.Refresh();
        var cpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / clock.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
        var (frames, render) = grid.RenderStatistics;
        line("jumps.average", jumps.Average(), "ms");
        line("jumps.worst", jumps.Max(), "ms");
        line("filters.average", filters.Average(), "ms");
        line("filters.worst", filters.Max(), "ms");
        line("realUse.cpu", cpu, "%");
        line("memory.realUsePeak", Math.Max(peak, process.WorkingSet64) / 1048576.0, "MB");
        line("grid.frameCost", frames > framesBefore ? (render - renderBefore) / (frames - framesBefore) : 0, "ms");
        var stats = model.Thumbnails.Statistics;
        line("thumbs.photos", stats.Photos, "");
        line("thumbs.photoAverage", stats.Photos == 0 ? 0 : stats.PhotoMs / stats.Photos, "ms");
        line("thumbs.videos", stats.Videos, "");
        line("thumbs.videoAverage", stats.Videos == 0 ? 0 : stats.VideoMs / stats.Videos, "ms");
        line("thumbs.audio", stats.Audio, "");
        line("thumbs.audioAverage", stats.Audio == 0 ? 0 : stats.AudioMs / stats.Audio, "ms");
        line("thumbs.diskHits", stats.DiskHits, "");
    }

    /// <summary>
    /// The player part of the benchmark: opens the first video (time until it plays), plays it looped for three
    /// seconds (CPU share of all cores, hardware decoding in use), then closes the viewer, which frees the render context
    /// before the player.
    /// </summary>
    private static async Task MediaSteps(MainWindow window, LibraryModel model, Action<string, double, string> line)
    {
        if (model.Refs.FirstOrDefault(r => r.Kind == Core.Model.MediaKind.Video) is not { Id: > 0 } video)
        {
            return;
        }
        var started = Stopwatch.StartNew();
        await window.OpenAsync(video.Id);
        if (window.MediaViewer?.Model is not { } media)
        {
            return;
        }
        await Until(() => media.Playback.Status is Viewer.Media.PlaybackStatus.Ready or Viewer.Media.PlaybackStatus.Failed);
        line("media.open", started.Elapsed.TotalMilliseconds, "ms");
        if (media.Playback.Status == Viewer.Media.PlaybackStatus.Ready)
        {
            media.Playback.ToggleLoop();
            using var process = Process.GetCurrentProcess();
            var cpuStart = process.TotalProcessorTime;
            var clock = Stopwatch.StartNew();
            await Task.Delay(3000);
            process.Refresh();
            line("media.playCpu", (process.TotalProcessorTime - cpuStart).TotalMilliseconds / clock.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100, "%");
            line("media.hardwareDecoding", media.Playback.Player.GetString("hwdec-current") is { Length: > 0 } and not "no" ? 1 : 0, "");
            line("memory.playingWorkingSet", process.WorkingSet64 / 1048576.0, "MB");
        }
        window.CloseViewer();
        await Task.Delay(500);
        line("media.closed", window.MediaViewer is null ? 1 : 0, "");
        await PreviewSteps(window, model, line);
    }

    /// <summary>
    /// The previews (W5): the first video selected (time until its paused first frame shows in the pane), Space (time
    /// until it plays in Quick Preview), → through the next files with Quick Preview open (time until each preview is
    /// complete), back to the video, Esc: the pane shows the video again, paused.
    /// </summary>
    private static async Task PreviewSteps(MainWindow window, LibraryModel model, Action<string, double, string> line)
    {
        if (window.Preview is not { } preview || model.Refs.FirstOrDefault(r => r.Kind == Core.Model.MediaKind.Video) is not { Id: > 0 } video)
        {
            return;
        }
        var grid = window.LibraryView.LibraryGrid;
        bool Ready() => preview.IsComplete && (!preview.OwnsPlayback || preview.Playback?.Status is Viewer.Media.PlaybackStatus.Ready or Viewer.Media.PlaybackStatus.Failed);
        bool FrameShown() => preview.Playback is { VideoSize.Width: > 0 } || preview.IsFailed;
        var clock = Stopwatch.StartNew();
        model.Selection.Select(video.Id);
        await Until(() => Ready() && FrameShown());
        line("preview.paneVideo", clock.Elapsed.TotalMilliseconds, "ms");
        line("preview.panePaused", preview.Playback is { IsPlaying: false } ? 1 : 0, "");

        clock.Restart();
        window.OpenQuickPreview();
        await Until(() => preview.Playback is { IsPlaying: true, CurrentTime: > 0 } || preview.IsFailed);
        line("preview.quickPreviewPlays", clock.Elapsed.TotalMilliseconds, "ms");

        var times = new List<double>();
        for (var step = 0; step < 8; step++)
        {
            clock.Restart();
            grid.MoveSelection(Core.Model.GridMove.Right);
            await Until(Ready);
            times.Add(clock.Elapsed.TotalMilliseconds);
            await Task.Delay(250);
        }
        times.Sort();
        line("preview.stepMedian", times[times.Count / 2], "ms");
        line("preview.stepWorst", times[^1], "ms");

        model.Selection.Select(video.Id);
        await Until(() => Ready() && preview.Playback is { IsPlaying: true });
        window.QuickPreview?.Dismiss();
        await Until(() => preview.Playback is { IsPlaying: false, Status: Viewer.Media.PlaybackStatus.Ready, VideoSize.Width: > 0 }
            && Avalonia.Controls.TopLevel.GetTopLevel(preview.Surface) == window);
        line("preview.backInPane", Avalonia.Controls.TopLevel.GetTopLevel(preview.Surface) == window && !preview.IsFailed ? 1 : 0, "");
        using var process = Process.GetCurrentProcess();
        line("memory.previewWorkingSet", process.WorkingSet64 / 1048576.0, "MB");
    }

    /// <summary>
    /// → held down (key repeat, a page every 33 ms) from the first photo through up to 40 pages, then until the last
    /// photo shows: CPU use over that time (share of all cores) and the peak working set.
    /// </summary>
    private static async Task HeldArrow(Viewer.PhotoViewerModel viewer, Action<string, double, string> line)
    {
        viewer.MoveTo(0);
        await Until(() => viewer.Failed || viewer.Picture is { IsPlaceholder: false });
        using var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();
        long peak = 0;
        for (var page = 0; page < 40 && viewer.CanMove(1); page++)
        {
            viewer.Move(1);
            await Task.Delay(33);
            process.Refresh();
            peak = Math.Max(peak, process.WorkingSet64);
        }
        await Until(() => viewer.Failed || viewer.Picture is { IsPlaceholder: false });
        process.Refresh();
        peak = Math.Max(peak, process.WorkingSet64);
        var cpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / clock.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
        line("viewer.heldArrowCpu", cpu, "%");
        line("viewer.heldArrowSeconds", clock.Elapsed.TotalSeconds, "s");
        line("memory.heldArrowPeak", peak / 1048576.0, "MB");
    }

    /// <summary>Waits a frame at a time until <paramref name="condition"/> holds (5 s at most).</summary>
    private static async Task Until(Func<bool> condition)
    {
        var started = Stopwatch.StartNew();
        while (!condition() && started.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(4);
        }
    }

    private static void Scroll(MainWindow window, Library.Grid.LibraryGrid grid, Action<string, double, string> line, Action done)
    {
        var frameBudget = 1000.0 / 60;
        TimeSpan? start = null;
        TimeSpan last = default;
        var frames = 0;
        var late = 0;
        var worst = 0.0;
        void Frame(TimeSpan now)
        {
            if (start is null)
            {
                start = last = now;
                window.RequestAnimationFrame(Frame);
                return;
            }
            var delta = (now - last).TotalMilliseconds;
            last = now;
            frames++;
            worst = Math.Max(worst, delta);
            if (delta > frameBudget * 1.5)
            {
                late++;
            }
            var maxOffset = Math.Max(0, grid.Extent.Height - grid.Viewport.Height);
            var y = grid.Offset.Y + ScrollSpeed * delta / 1000;
            grid.Offset = new Vector(0, y > maxOffset ? 0 : y);
            grid.RaiseScrollInvalidated(EventArgs.Empty);
            if (now - start.Value < ScrollDuration)
            {
                window.RequestAnimationFrame(Frame);
                return;
            }
            line("scroll.frames", frames, "frames");
            line("scroll.lateFrames", late, "frames");
            line("scroll.lateShare", frames == 0 ? 0 : 100.0 * late / frames, "%");
            line("scroll.worstFrame", worst, "ms");
            done();
        }
        window.RequestAnimationFrame(Frame);
    }
}
