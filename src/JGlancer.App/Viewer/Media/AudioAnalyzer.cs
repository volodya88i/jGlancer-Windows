using JGlancer.Core.Audio;
using JGlancer.Core.Store;
using JGlancer.Mpv;

namespace JGlancer.App.Viewer.Media;

/// <summary>Tempo and key of a song (W-107): remembered in the library, found on request from the waveform's samples.</summary>
internal static class AudioAnalyzer
{
    /// <summary>The library's store; set when the app starts.</summary>
    public static LibraryStore? Store { get; set; }

    /// <summary>What was found earlier in this version of the file, or null.</summary>
    public static async Task<AudioAnalysis?> KnownAsync(string path)
    {
        if (Store is not { } store || !Version(path, out var size, out var ticks))
        {
            return null;
        }
        return await store.AudioAnalysisAsync(path, size, ticks).ConfigureAwait(false);
    }

    /// <summary>Decodes and analyzes the file off the UI thread and keeps the result; null when it has no usable audio.</summary>
    public static async Task<AudioAnalysis?> AnalyzeAsync(string path, CancellationToken cancellation)
    {
        var samples = await MpvAudioDecoder.SamplesAsync(path, cancellation).ConfigureAwait(false);
        if (samples is null)
        {
            return null;
        }
        var analysis = await Task.Run(() => new AudioAnalysis(
            TempoDetector.Detect(samples, MpvAudioDecoder.SamplesPerSecond),
            KeyDetector.Detect(samples, MpvAudioDecoder.SamplesPerSecond)), cancellation).ConfigureAwait(false);
        if (analysis is { Bpm: null, Key: null })
        {
            return null;
        }
        await SaveAsync(path, analysis).ConfigureAwait(false);
        return analysis;
    }

    /// <summary>Keeps <paramref name="analysis"/> for the file as it is now.</summary>
    public static async Task SaveAsync(string path, AudioAnalysis analysis)
    {
        if (Store is { } store && Version(path, out var size, out var ticks))
        {
            await store.SaveAudioAnalysisAsync(path, size, ticks, analysis).ConfigureAwait(false);
        }
    }

    private static bool Version(string path, out long size, out long ticks)
    {
        var file = new FileInfo(path);
        (size, ticks) = file.Exists ? (file.Length, file.LastWriteTimeUtc.Ticks) : (0, 0);
        return file.Exists;
    }
}
