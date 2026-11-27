using Microsoft.ML.OnnxRuntime;

namespace JGlancer.Stems;

/// <summary>Opens a stem network's ONNX session: DirectML on a DirectX 12 GPU, else the CPU (W-85).</summary>
internal static class OnnxSessions
{
    /// <summary>Opens <paramref name="path"/>: on the GPU unless <paramref name="cpuOnly"/> or DirectML fails.</summary>
    public static (InferenceSession Session, bool UsesGpu) Open(string path, bool cpuOnly)
    {
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
                // DirectML's graph fusion computes Demucs and RoFormer wrongly (noise instead of stems, 9 dB from
                // PyTorch; measured on a GTX 1070 with ONNX Runtime 1.24, W-120): the layers run one by one, exactly.
                gpu.AddSessionConfigEntry("ep.dml.disable_graph_fusion", "1");
                gpu.AppendExecutionProvider_DML(0);
                return (new InferenceSession(path, gpu), true);
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
        return (new InferenceSession(path, cpu), false);
    }
}
