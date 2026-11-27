using JGlancer.Core.Stems;
using Microsoft.ML.OnnxRuntime;

namespace JGlancer.Stems;

/// <summary>
/// A stem network as an <see cref="IStemSeparator"/>: Demucs (W-85: the C# spectrogram, the ONNX network, the C# inverse
/// plus the time branch) or Mel-Band RoFormer (MAC-056: <see cref="RoFormerSeparator"/> around the ONNX network). One
/// session per split run: it holds hundreds of MB (RoFormer about a GB), so it is disposed right after.
/// </summary>
public sealed class OnnxStemSeparator : IStemSeparator, IDisposable
{
    private readonly InferenceSession _session;
    private readonly IStemSeparator _inner;

    private OnnxStemSeparator(InferenceSession session, StemModelInfo model, bool usesGpu)
    {
        _session = session;
        Model = model;
        UsesGpu = usesGpu;
        _inner = model.Network == StemNetwork.Vocals ? new RoFormerSeparator(new BandMasks(session)) : new Demucs(session, model);
    }

    public StemModelInfo Model { get; }

    /// <summary>Whether the network runs on the GPU (DirectML).</summary>
    public bool UsesGpu { get; }

    public int Sources => _inner.Sources;

    public int SegmentFrames => _inner.SegmentFrames;

    public double Overlap => _inner.Overlap;

    public bool Normalizes => _inner.Normalizes;

    /// <summary>Opens <paramref name="path"/>: on the GPU unless <paramref name="cpuOnly"/> or DirectML fails.</summary>
    public static OnnxStemSeparator Open(string path, StemModelInfo model, bool cpuOnly = false)
    {
        ArgumentNullException.ThrowIfNull(model);
        var (session, usesGpu) = OnnxSessions.Open(path, cpuOnly);
        return new OnnxStemSeparator(session, model, usesGpu);
    }

    public void Separate(ReadOnlySpan<float> input, Span<float> output) => _inner.Separate(input, output);

    public void Dispose() => _session.Dispose();

    /// <summary>Demucs: the spectrogram and the waveform in, each source's spectrogram and time branch out.</summary>
    private sealed class Demucs(InferenceSession session, StemModelInfo model) : IStemSeparator
    {
        private readonly int _frames = Spectrogram.Frames(model.SegmentFrames);
        private readonly float[] _spec = new float[4 * Spectrogram.Bins * Spectrogram.Frames(model.SegmentFrames)];

        public int Sources => model.Sources.Count;

        public int SegmentFrames => model.SegmentFrames;

        public void Separate(ReadOnlySpan<float> input, Span<float> output)
        {
            var length = SegmentFrames;
            Spectrogram.Forward(input, length, _spec);
            var mix = input.ToArray();
            using var mixValue = OrtValue.CreateTensorValueFromMemory(mix, [1, 2, length]);
            using var specValue = OrtValue.CreateTensorValueFromMemory(_spec, [1, 4, Spectrogram.Bins, _frames]);
            using var options = new RunOptions();
            using var results = session.Run(options, ["mix", "spec"], [mixValue, specValue], ["freq", "time"]);
            // The time branch first, then each source's spectrogram inverted on top of it.
            results[1].GetTensorDataAsSpan<float>().CopyTo(output);
            Spectrogram.InverseAdd(results[0].GetTensorDataAsSpan<float>(), Sources, length, output);
        }
    }

    /// <summary>RoFormer's network: the mel bands' bins in, their complex masks out.</summary>
    private sealed class BandMasks(InferenceSession session) : IBandMaskNetwork
    {
        public void Run(float[] bands, Span<float> masks)
        {
            using var input = OrtValue.CreateTensorValueFromMemory(bands, [1, bands.Length / MelBands.Features, MelBands.Features]);
            using var options = new RunOptions();
            using var results = session.Run(options, ["bands"], [input], ["masks"]);
            results[0].GetTensorDataAsSpan<float>().CopyTo(masks);
        }
    }
}
