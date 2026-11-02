using JGlancer.Core.Stems;
using Microsoft.ML.OnnxRuntime;

namespace JGlancer.Stems;

/// <summary>
/// Demucs (W-85) as an <see cref="IStemSeparator"/>: the C# spectrogram, the ONNX network (DirectML on a DirectX 12
/// GPU, else the CPU), the C# inverse plus the time branch. One session per split: it holds hundreds of MB, so it is
/// disposed right after.
/// </summary>
public sealed class OnnxStemSeparator : IStemSeparator, IDisposable
{
    private readonly InferenceSession _session;
    private readonly int _frames;
    private readonly float[] _spec;

    private OnnxStemSeparator(InferenceSession session, StemModelInfo model, bool usesGpu)
    {
        _session = session;
        Model = model;
        UsesGpu = usesGpu;
        _frames = Spectrogram.Frames(model.SegmentFrames);
        _spec = new float[4 * Spectrogram.Bins * _frames];
    }

    public StemModelInfo Model { get; }

    /// <summary>Whether the network runs on the GPU (DirectML).</summary>
    public bool UsesGpu { get; }

    public int Sources => Model.Sources.Count;

    public int SegmentFrames => Model.SegmentFrames;

    /// <summary>Opens <paramref name="path"/>: on the GPU unless <paramref name="cpuOnly"/> or DirectML fails.</summary>
    public static OnnxStemSeparator Open(string path, StemModelInfo model, bool cpuOnly = false)
    {
        ArgumentNullException.ThrowIfNull(model);
        // No telemetry (CLAUDE.md): ONNX Runtime's Windows builds raise usage events otherwise.
        OrtEnv.Instance().DisableTelemetryEvents();
        if (!cpuOnly)
        {
            try
            {
                using var gpu = new SessionOptions
                {
                    // DirectML wants these (ONNX Runtime's DirectML notes).
                    EnableMemoryPattern = false,
                    ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                };
                // DirectML's graph fusion computes this network wrongly (noise instead of stems; measured on a
                // GTX 1070 with ONNX Runtime 1.24): the layers run one by one, exactly.
                gpu.AddSessionConfigEntry("ep.dml.disable_graph_fusion", "1");
                gpu.AppendExecutionProvider_DML(0);
                return new OnnxStemSeparator(new InferenceSession(path, gpu), model, usesGpu: true);
            }
            catch (OnnxRuntimeException)
            {
                // No DirectX 12 GPU, or a driver DirectML cannot use: the CPU does it.
            }
        }
        // Without the arena and memory pattern the CPU keeps ~1 GB instead of growing to several (measured, W-85).
        using var cpu = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            EnableCpuMemArena = false,
            EnableMemoryPattern = false,
        };
        return new OnnxStemSeparator(new InferenceSession(path, cpu), model, usesGpu: false);
    }

    public void Separate(ReadOnlySpan<float> input, Span<float> output)
    {
        var length = SegmentFrames;
        Spectrogram.Forward(input, length, _spec);
        var mix = input.ToArray();
        using var mixValue = OrtValue.CreateTensorValueFromMemory(mix, [1, 2, length]);
        using var specValue = OrtValue.CreateTensorValueFromMemory(_spec, [1, 4, Spectrogram.Bins, _frames]);
        using var options = new RunOptions();
        using var results = _session.Run(options, ["mix", "spec"], [mixValue, specValue], ["freq", "time"]);
        // The time branch first, then each source's spectrogram inverted on top of it.
        results[1].GetTensorDataAsSpan<float>().CopyTo(output);
        Spectrogram.InverseAdd(results[0].GetTensorDataAsSpan<float>(), Sources, length, output);
    }

    public void Dispose() => _session.Dispose();
}
