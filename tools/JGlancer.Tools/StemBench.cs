using System.Diagnostics;
using JGlancer.Core.Stems;
using JGlancer.Stems;

namespace JGlancer.Tools;

/// <summary>
/// <c>stems &lt;model.onnx&gt; four|six &lt;song&gt; &lt;folder&gt; [--cpu]</c>: splits a song as the app does and reports the
/// time of each step and the peak memory (W-85 budgets). Writes every stem into the folder.
/// </summary>
internal static class StemBench
{
    public static async Task<int> RunAsync(string modelPath, string mode, string song, string folder, bool cpuOnly)
    {
        var model = StemModelInfo.For(mode == "six" ? StemMode.Six : StemMode.Four);
        var clock = Stopwatch.StartNew();
        var audio = await JGlancer.Mpv.MpvAudioDecoder.StereoAsync(song, model.SampleRate, CancellationToken.None);
        if (audio is null)
        {
            Console.Error.WriteLine("cannot decode " + song);
            return 1;
        }
        var frames = (int)audio.Frames;
        var left = new float[frames];
        var right = new float[frames];
        var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(audio.Data);
        for (var i = 0; i < frames; i++)
        {
            left[i] = samples[2 * i];
            right[i] = samples[2 * i + 1];
        }
        Console.WriteLine($"decoded {audio.Seconds:0.0} s in {clock.Elapsed.TotalSeconds:0.00} s");
        clock.Restart();
        using var separator = OnnxStemSeparator.Open(modelPath, model, cpuOnly);
        Console.WriteLine($"session ({(separator.UsesGpu ? "DirectML" : "CPU")}) in {clock.Elapsed.TotalSeconds:0.00} s");
        Directory.CreateDirectory(folder);
        // The decoded input, for comparing with Demucs itself (scripts/stems/compare_demucs.py).
        await File.WriteAllBytesAsync(Path.Combine(folder, "_input.wav"), JGlancer.Core.Playback.PcmFiles.WriteWave(audio));
        var writers = new Dictionary<int, IStemSink>();
        for (var source = 0; source < model.Sources.Count; source++)
        {
            var path = Path.Combine(folder, StemNaming.FileName(song, model.Sources[source]));
            writers[source] = new WaveStemWriter(File.Create(path), model.SampleRate);
        }
        clock.Restart();
        var peak = 0L;
        StemSplitter.Split(left, right, separator, writers, done =>
        {
            peak = Math.Max(peak, Process.GetCurrentProcess().WorkingSet64);
            Console.Write($"\r{done:P0}  {clock.Elapsed.TotalSeconds:0.0} s   ");
        }, CancellationToken.None);
        Console.WriteLine();
        foreach (var writer in writers.Values.Cast<WaveStemWriter>())
        {
            writer.Complete();
            writer.Dispose();
        }
        Console.WriteLine($"split {audio.Seconds:0.0} s of audio in {clock.Elapsed.TotalSeconds:0.0} s, peak working set {peak / 1e6:0} MB");
        return 0;
    }
}
