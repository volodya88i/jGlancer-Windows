using JGlancer.Core.Stems;

namespace JGlancer.Stems.Tests;

/// <summary>
/// The app's whole RoFormer pipeline (C# spectrogram, ONNX network, mask averaging, inverse, half-overlapping pieces)
/// against PyTorch's: runs only on a developer machine with <c>JG_ROFORMER_MODEL</c> (an .onnx) and
/// <c>JG_ROFORMER_REFERENCE</c> (the folder <c>convert_roformer.py … reference</c> wrote); <c>JG_ROFORMER_CPU=1</c>
/// keeps it off the GPU.
/// </summary>
public sealed class RoFormerModelTests
{
    [Fact]
    public void TheVocalsAreThoseOfPyTorch()
    {
        var model = Environment.GetEnvironmentVariable("JG_ROFORMER_MODEL");
        var folder = Environment.GetEnvironmentVariable("JG_ROFORMER_REFERENCE");
        Assert.SkipWhen(model is null || folder is null, "JG_ROFORMER_MODEL and JG_ROFORMER_REFERENCE are not set");
        var mix = Floats(Path.Combine(folder!, "reference.f32"));
        var expected = Floats(Path.Combine(folder!, "reference-vocals.f32"));
        var frames = mix.Length / 2;
        var left = mix[..frames];
        var right = mix[frames..];
        var info = model!.Contains("-full", StringComparison.Ordinal) ? StemModelInfo.RoFormerFull : StemModelInfo.RoFormer;
        using var separator = OnnxStemSeparator.Open(model, info, cpuOnly: Environment.GetEnvironmentVariable("JG_ROFORMER_CPU") == "1");
        var vocals = new Collect();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        StemSplitter.Split(left, right, separator, [new(info.Part(StemKind.Vocals)!.Value, vocals)], null, CancellationToken.None);
        double signal = 0, noise = 0;
        for (var i = 0; i < frames; i++)
        {
            signal += expected[i] * expected[i] + expected[frames + i] * expected[frames + i];
            var (dl, dr) = (vocals.Left[i] - expected[i], vocals.Right[i] - expected[frames + i]);
            noise += dl * dl + dr * dr;
        }
        var decibels = 10 * Math.Log10(signal / Math.Max(noise, 1e-30));
        var line = $"{Path.GetFileName(model)} on {(separator.UsesGpu ? "DirectML" : "CPU")}: {decibels:0.0} dB against PyTorch, {clock.Elapsed.TotalSeconds:0.0} s";
        TestContext.Current.TestOutputHelper?.WriteLine(line);
        if (Environment.GetEnvironmentVariable("JG_ROFORMER_LOG") is { } log)
        {
            File.AppendAllLines(log, [line]);
        }
        // Measured (W-120): full 135 dB, compact 80 dB, as on the Mac (D-131); both far below hearing.
        Assert.True(decibels >= (info.IsFull ? 120 : 75), $"{decibels:0.0} dB");
    }

    private static float[] Floats(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var values = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }

    private sealed class Collect : IStemSink
    {
        public List<float> Left { get; } = [];

        public List<float> Right { get; } = [];

        public void Write(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
        {
            Left.AddRange(left.ToArray());
            Right.AddRange(right.ToArray());
        }
    }
}
