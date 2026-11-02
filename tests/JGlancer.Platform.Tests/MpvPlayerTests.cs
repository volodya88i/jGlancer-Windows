using JGlancer.Core.Tests.Support;
using JGlancer.Mpv;

namespace JGlancer.Platform.Tests;

/// <summary>libmpv end to end on the fixtures (skipped when scripts\deps.ps1 has not fetched it).</summary>
public sealed class MpvPlayerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void AudioLoadsKnowsItsDurationAndStaysAtTheEnd()
    {
        Assert.SkipUnless(MpvPlayer.IsAvailable, "libmpv is not in build\\deps (run scripts\\deps.ps1)");
        using var folder = new TemporaryDirectory();
        Fixtures.CopyTo(folder, "tone_440_mono.wav");
        using var player = new MpvPlayer(video: false);
        using var loaded = new ManualResetEventSlim();
        using var ended = new ManualResetEventSlim();
        double duration = 0;
        player.DurationChanged += seconds => duration = seconds;
        player.FileLoaded += loaded.Set;
        player.EndReached += ended.Set;
        player.Load(folder.Full("tone_440_mono.wav"), paused: true);
        Assert.True(loaded.Wait(Timeout));
        Assert.InRange(player.Duration ?? duration, 1.5, 2.5);
        // Near the end, played at 4×: it reaches the end and stays there (keep-open) instead of unloading.
        player.SetSpeed(4);
        player.Seek((player.Duration ?? duration) - 0.3, exact: true);
        player.SetPaused(false);
        Assert.True(ended.Wait(Timeout));
        Assert.NotNull(player.Position);
    }

    [Fact]
    public async Task AudioDecodesToWaveformPeaks()
    {
        Assert.SkipUnless(MpvPlayer.IsAvailable, "libmpv is not in build\\deps (run scripts\\deps.ps1)");
        using var folder = new TemporaryDirectory();
        Fixtures.CopyTo(folder, "tone_440_mono.wav");
        Fixtures.CopyTo(folder, "tone_with_cover.m4a");
        Fixtures.CopyTo(folder, "empty.mp4");
        // A steady tone: every bucket swings well away from zero, both ways.
        var tone = await MpvAudioDecoder.PeaksAsync(folder.Full("tone_440_mono.wav"), TestContext.Current.CancellationToken);
        Assert.NotNull(tone);
        Assert.Equal(Core.Playback.WaveformMath.Buckets, tone.Value.Highs.Length);
        Assert.True(tone.Value.Highs.Average() > 0.2 && tone.Value.Lows.Average() < -0.2);
        Assert.NotNull(await MpvAudioDecoder.PeaksAsync(folder.Full("tone_with_cover.m4a"), TestContext.Current.CancellationToken));
        Assert.Null(await MpvAudioDecoder.PeaksAsync(folder.Full("empty.mp4"), TestContext.Current.CancellationToken));
        // Nothing is left behind in the temp folder.
        Assert.Empty(Directory.GetFiles(Path.GetTempPath(), "jGlancer-waveform-*.wav"));
    }

    [Fact]
    public void BrokenFilesFailAndVideoReportsItsSize()
    {
        Assert.SkipUnless(MpvPlayer.IsAvailable, "libmpv is not in build\\deps (run scripts\\deps.ps1)");
        using var folder = new TemporaryDirectory();
        Fixtures.CopyTo(folder, "empty.mp4");
        Fixtures.CopyTo(folder, "clip_with_audio.mp4");
        using (var player = new MpvPlayer(video: false))
        {
            using var failed = new ManualResetEventSlim();
            player.Ended += reason =>
            {
                if (reason == MpvEndReason.Error)
                {
                    failed.Set();
                }
            };
            player.Load(folder.Full("empty.mp4"));
            Assert.True(failed.Wait(Timeout), "the empty file did not fail");
        }
        // Without a render context mpv shows no picture (the app always has one), but a file with sound loads and the
        // container knows the picture size.
        using var video = new MpvPlayer(video: true);
        using var loaded = new ManualResetEventSlim();
        video.FileLoaded += loaded.Set;
        video.Load(folder.Full("clip_with_audio.mp4"), paused: true);
        Assert.True(loaded.Wait(Timeout), "the video did not load");
        Assert.True(int.Parse(video.GetString("track-list/0/demux-w") ?? "0", System.Globalization.CultureInfo.InvariantCulture) > 0);
    }
}
