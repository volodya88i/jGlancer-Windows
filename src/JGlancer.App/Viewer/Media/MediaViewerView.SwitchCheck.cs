using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Threading;

namespace JGlancer.App.Viewer.Media;

/// <summary>
/// `--switch-check &lt;log&gt;` (with `--open-first --open-kind video`): moves through the videos of the folder as a click on
/// the filmstrip does and logs, per file, how long the call took, when mpv had the file, when the first frame was drawn,
/// and the longest time the UI thread did not respond. mpv's own log goes beside it (`.mpv.log`). Quits.
/// </summary>
internal sealed partial class MediaViewerView
{
    public async Task RunSwitchCheckAsync(string log)
    {
        const int Switches = 12;
        await Task.Delay(TimeSpan.FromSeconds(3));
        if (_model is not { } model || _video is not { } video)
        {
            return;
        }
        var player = model.Playback.Player;
        player.SetLogFile(log + ".mpv.log");
        var lines = new List<string> { "file | MB | call | loaded | first frame | UI stall (ms)" };
        var clock = Stopwatch.StartNew();
        double loaded = 0, frame = 0;
        void OnLoaded() => Volatile.Write(ref loaded, clock.Elapsed.TotalMilliseconds);
        void OnFrame()
        {
            if (Volatile.Read(ref loaded) > 0 && Volatile.Read(ref frame) == 0)
            {
                Volatile.Write(ref frame, clock.Elapsed.TotalMilliseconds);
            }
        }
        player.FileLoaded += OnLoaded;
        video.FrameDrawn += OnFrame;
        var last = clock.Elapsed.TotalMilliseconds;
        var stall = 0.0;
        using var beat = new DispatcherTimerGuard(TimeSpan.FromMilliseconds(4), () =>
        {
            var now = clock.Elapsed.TotalMilliseconds;
            stall = Math.Max(stall, now - last);
            last = now;
        });
        var count = Math.Min(Switches, model.Count - 1);
        for (var i = 1; i <= count; i++)
        {
            (loaded, frame, stall) = (0, 0, 0);
            last = clock.Elapsed.TotalMilliseconds;
            var start = last;
            model.MoveTo(i);
            var call = clock.Elapsed.TotalMilliseconds - start;
            for (var wait = 0; wait < 100 && Volatile.Read(ref frame) == 0; wait++)
            {
                await Task.Delay(50);
            }
            var firstStall = stall;
            var item = model.Current;
            var megabytes = item is null ? 0 : new FileInfo(item.Path).Length / (1 << 20);
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"{item?.Name} | {megabytes} | {call:0} | {loaded - start:0} | {frame - start:0} | {firstStall:0}"));
            await Task.Delay(1000);
        }
        player.FileLoaded -= OnLoaded;
        video.FrameDrawn -= OnFrame;
        await File.WriteAllLinesAsync(log, lines);
        (TopLevel.GetTopLevel(this) as Window)?.Close();
    }

    /// <summary>A UI-thread heartbeat for the check's lifetime.</summary>
    private sealed class DispatcherTimerGuard : IDisposable
    {
        private readonly DispatcherTimer _timer;

        public DispatcherTimerGuard(TimeSpan interval, Action tick)
        {
            _timer = new DispatcherTimer(interval, DispatcherPriority.Input, (_, _) => tick());
            _timer.Start();
        }

        public void Dispose() => _timer.Stop();
    }
}
