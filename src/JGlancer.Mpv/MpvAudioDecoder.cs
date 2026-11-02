using JGlancer.Core.Playback;

namespace JGlancer.Mpv;

/// <summary>
/// Decodes a file's audio for its waveform (W-34): a headless mpv with the PCM output writes 8 kHz mono 16-bit samples
/// to a temporary WAVE file as fast as it can decode (no playback, no clock), then the file is reduced to peaks and
/// deleted. Every format the player plays gets a waveform the same way.
/// </summary>
public static class MpvAudioDecoder
{
    private const int SampleRate = 8000;

    /// <summary>Peaks of <paramref name="path"/>'s audio, or null when it has none or cannot be decoded.</summary>
    public static async Task<(float[] Lows, float[] Highs)?> PeaksAsync(string path, CancellationToken cancellation) =>
        await SamplesAsync(path, cancellation).ConfigureAwait(false) is { } samples ? WaveformMath.Peaks(samples) : null;

    /// <summary>Samples per second of <see cref="SamplesAsync"/>.</summary>
    public const int SamplesPerSecond = SampleRate;

    /// <summary>
    /// The file's audio as 8 kHz mono 16-bit samples (about 1 MB a minute): the waveform, and finer peaks of any
    /// zoomed-in part without decoding again. Null when it has none or cannot be decoded.
    /// </summary>
    public static async Task<short[]?> SamplesAsync(string path, CancellationToken cancellation)
    {
        var output = Path.Combine(Path.GetTempPath(), "jGlancer-waveform-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            if (!await DecodeAsync(path, output, SampleRate, "mono", "s16", cancellation).ConfigureAwait(false) || !File.Exists(output))
            {
                return null;
            }
            var samples = WaveformMath.WaveSamples(await File.ReadAllBytesAsync(output, cancellation).ConfigureAwait(false));
            return samples.Length == 0 ? null : samples;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            try
            {
                File.Delete(output);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// The file's audio as stereo 32-bit float at <paramref name="sampleRate"/> (mono doubled, more channels mixed
    /// down): the stem splitter's input (W-85). Null when it has none or cannot be decoded.
    /// </summary>
    public static async Task<PcmAudio?> StereoAsync(string path, int sampleRate, CancellationToken cancellation)
    {
        var output = Path.Combine(Path.GetTempPath(), "jGlancer-stems-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            if (!await DecodeAsync(path, output, sampleRate, "stereo", "float", cancellation).ConfigureAwait(false) || !File.Exists(output))
            {
                return null;
            }
            var audio = PcmFiles.ReadWave(await File.ReadAllBytesAsync(output, cancellation).ConfigureAwait(false));
            return audio is { IsFloat: true, Channels: 2 } && audio.Frames > 0 ? audio : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            try
            {
                File.Delete(output);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>What a file's audio is: codec ("pcm_s24le", "flac", "aac"…), sample rate, channels, sample format.</summary>
    public sealed record AudioFacts(string Codec, int SampleRate, int Channels, string SampleFormat);

    /// <summary>The facts of <paramref name="path"/>'s audio, or null when it has none.</summary>
    public static async Task<AudioFacts?> ProbeAsync(string path, CancellationToken cancellation)
    {
        var loaded = new TaskCompletionSource<AudioFacts?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var player = new MpvPlayer([("ao", "null")]);
        player.FileLoaded += () => _ = Task.Run(async () =>
        {
            static int Number(string? text) => int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
            // The decoder sets the audio parameters a moment after the file opens.
            for (var attempt = 0; attempt < 200 && !loaded.Task.IsCompleted; attempt++)
            {
                var rate = Number(player.GetString("audio-params/samplerate"));
                if (rate > 0)
                {
                    var codec = player.GetString("audio-codec-name") ?? "";
                    var channels = Number(player.GetString("audio-params/channel-count"));
                    var format = player.GetString("audio-params/format") ?? "";
                    loaded.TrySetResult(new AudioFacts(codec, rate, Math.Max(1, channels), format));
                    return;
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
            loaded.TrySetResult(null);
        });
        player.Ended += _ => loaded.TrySetResult(null);
        using var registration = cancellation.Register(() => loaded.TrySetResult(null));
        // Paused: the decoder starts, so the audio parameters are known, but nothing plays.
        player.Load(path, paused: true);
        var result = await loaded.Task.ConfigureAwait(false);
        if (result is not null && result.SampleRate == 0)
        {
            return null;
        }
        return result;
    }

    private static async Task<bool> DecodeAsync(string path, string output, int sampleRate, string channels, string format, CancellationToken cancellation)
    {
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var player = new MpvPlayer(
        [
            ("ao", "pcm"),
            ("ao-pcm-file", output),
            ("ao-pcm-waveheader", "yes"),
            ("audio-samplerate", sampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("audio-channels", channels),
            ("audio-format", format),
            ("untimed", "yes"),
            ("keep-open", "no"),
        ]);
        player.Ended += reason => finished.TrySetResult(reason == MpvEndReason.EndOfFile);
        using var registration = cancellation.Register(() => finished.TrySetResult(false));
        player.Load(path);
        return await finished.Task.ConfigureAwait(false);
    }
}
