using JGlancer.Core.Model;
using JGlancer.Core.Playback;
using JGlancer.Core.Tests.Support;
using JGlancer.Mpv;
using JGlancer.Platform.Windows;

namespace JGlancer.Platform.Tests;

/// <summary>Segments of the fixtures, written to a temp folder and read back with mpv.</summary>
public sealed class SegmentExporterTests
{
    private static async Task<double> DurationOf(string path)
    {
        using var player = new MpvPlayer(video: false);
        var loaded = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
        player.FileLoaded += () => loaded.TrySetResult(player.Duration ?? 0);
        player.Ended += reason => loaded.TrySetResult(-1);
        player.Load(path, paused: true);
        return await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData("tone_440_mono.wav", true, "wav")]
    [InlineData("tone_stereo.aiff", true, "aiff")]
    [InlineData("tone_440_mono.wav", false, "m4a")]
    [InlineData("tone_with_cover.m4a", true, "m4a")]
    public async Task AudioSegmentsHaveTheExactLengthInTheExpectedFormat(string fixture, bool keepsFormat, string extension)
    {
        Assert.SkipUnless(MpvPlayer.IsAvailable, "libmpv is not in build\\deps (run scripts\\deps.ps1)");
        using var folder = new TemporaryDirectory();
        Fixtures.CopyTo(folder, fixture);
        var request = new SegmentExportRequest(folder.Full(fixture), MediaKind.Audio, new SegmentRange(0.4, 1.4), Fades: true, KeepsAudioFormat: keepsFormat);
        Assert.Equal(extension, Segments.FileExtension(request));
        var target = folder.Full("out." + extension);
        await SegmentExporter.ExportAsync(request, target, TestContext.Current.CancellationToken);
        Assert.Equal(1.0, await DurationOf(target), 1);
    }

    [Fact]
    public async Task MediaFoundationWritesFlac()
    {
        Assert.SkipUnless(MpvPlayer.IsAvailable, "libmpv is not in build\\deps (run scripts\\deps.ps1)");
        using var folder = new TemporaryDirectory();
        // One second of a 440 Hz tone, 24-bit stereo at 48 kHz.
        var data = new byte[48000 * 2 * 3];
        for (var frame = 0; frame < 48000; frame++)
        {
            var value = (int)(Math.Sin(frame * 440 * 2 * Math.PI / 48000) * 4_000_000);
            for (var channel = 0; channel < 2; channel++)
            {
                var at = (frame * 2 + channel) * 3;
                data[at] = (byte)value;
                data[at + 1] = (byte)(value >> 8);
                data[at + 2] = (byte)(value >> 16);
            }
        }
        var target = folder.Full("tone.flac");
        MediaFoundation.EncodeAudio(new PcmAudio(48000, 2, 24, false, data), target, AudioEncoding.Flac);
        Assert.Equal(1.0, await DurationOf(target), 1);
    }

    [Fact]
    public async Task MediaFoundationWritesMp3()
    {
        Assert.SkipUnless(MpvPlayer.IsAvailable, "libmpv is not in build\\deps (run scripts\\deps.ps1)");
        using var folder = new TemporaryDirectory();
        // One second of a 440 Hz tone, 16-bit stereo at 44.1 kHz (what the decode step gives MP3).
        var data = new byte[44100 * 2 * 2];
        for (var frame = 0; frame < 44100; frame++)
        {
            var value = (short)(Math.Sin(frame * 440 * 2 * Math.PI / 44100) * 8000);
            for (var channel = 0; channel < 2; channel++)
            {
                var at = (frame * 2 + channel) * 2;
                data[at] = (byte)value;
                data[at + 1] = (byte)(value >> 8);
            }
        }
        var target = folder.Full("tone.mp3");
        MediaFoundation.EncodeAudio(new PcmAudio(44100, 2, 16, false, data), target, AudioEncoding.Mp3);
        Assert.Equal(1.0, await DurationOf(target), 1);
        // A faster segment of an MP3 stays MP3.
        var request = new SegmentExportRequest(target, MediaKind.Audio, new SegmentRange(0.2, 0.8), Rate: 1.5);
        Assert.Equal("mp3", Segments.FileExtension(request));
        var segment = folder.Full("fast.mp3");
        await SegmentExporter.ExportAsync(request, segment, TestContext.Current.CancellationToken);
        Assert.Equal(0.4, await DurationOf(segment), 1);
    }

    [Fact]
    public async Task SpeedShortensTheSegmentAndWavesAreCutToTheSample()
    {
        Assert.SkipUnless(MpvPlayer.IsAvailable, "libmpv is not in build\\deps (run scripts\\deps.ps1)");
        using var folder = new TemporaryDirectory();
        Fixtures.CopyTo(folder, "tone_440_mono.wav");
        var request = new SegmentExportRequest(folder.Full("tone_440_mono.wav"), MediaKind.Audio, new SegmentRange(0.2, 1.2), Rate: 2);
        var target = folder.Full("fast.wav");
        await SegmentExporter.ExportAsync(request, target, TestContext.Current.CancellationToken);
        var pcm = PcmFiles.ReadWave(await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken))!;
        Assert.Equal(0.5, pcm.Seconds, 3);
    }

    [Fact]
    public async Task VideoIsCopiedIntoMp4AndSavingNeverReplaces()
    {
        Assert.SkipUnless(MpvPlayer.IsAvailable, "libmpv is not in build\\deps (run scripts\\deps.ps1)");
        using var folder = new TemporaryDirectory();
        Fixtures.CopyTo(folder, "clip_with_audio.mp4");
        var request = new SegmentExportRequest(folder.Full("clip_with_audio.mp4"), MediaKind.Video, new SegmentRange(0.5, 1.5));
        var first = await SegmentExporter.SaveAsync(request, folder.Full("cut.mp4"), TestContext.Current.CancellationToken);
        var second = await SegmentExporter.SaveAsync(request, folder.Full("cut.mp4"), TestContext.Current.CancellationToken);
        Assert.Equal(folder.Full("cut.mp4"), first);
        Assert.Equal(folder.Full("cut 2.mp4"), second);
        // From the keyframe at or before 0.5 s to 1.5 s.
        Assert.InRange(await DurationOf(first), 0.9, 1.6);
        // The temporary folders SaveAsync made are gone: no export folder still holds a "cut.mp4".
        Assert.DoesNotContain(Directory.GetDirectories(Path.GetTempPath(), "jGlancer-export-*"), d => File.Exists(Path.Combine(d, "cut.mp4")));
    }
}
