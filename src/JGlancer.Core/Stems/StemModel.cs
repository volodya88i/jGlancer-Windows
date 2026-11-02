namespace JGlancer.Core.Stems;

/// <summary>A part of a song that the splitter writes as its own file (macOS D-94, W-85).</summary>
public enum StemKind
{
    Vocals,
    Drums,
    Bass,
    Guitar,
    Piano,
    Other,
}

/// <summary>4 stems (vocals, drums, bass, the rest) or 6 (also guitar and piano).</summary>
public enum StemMode
{
    Four,
    Six,
}

/// <summary>
/// One Demucs model as the app uses it: its file, the order of the sources it produces (Demucs' own order), its sample
/// rate and the length of the piece of audio it takes at once. <paramref name="IsFull"/>: the weights at full precision
/// (twice the download); otherwise compact, half-precision weights computing at full precision (W-92).
/// </summary>
public sealed record StemModelInfo(StemMode Mode, string FileName, IReadOnlyList<StemKind> Sources, int SampleRate, int SegmentFrames, bool IsFull = false)
{
    /// <summary>Demucs v4 Hybrid Transformer, 4 sources (htdemucs).</summary>
    public static StemModelInfo Four { get; } = new(StemMode.Four, "htdemucs.onnx",
        [StemKind.Drums, StemKind.Bass, StemKind.Other, StemKind.Vocals], 44_100, 343_980);

    /// <summary>Demucs v4 Hybrid Transformer, 6 sources (htdemucs_6s).</summary>
    public static StemModelInfo Six { get; } = new(StemMode.Six, "htdemucs_6s.onnx",
        [StemKind.Drums, StemKind.Bass, StemKind.Other, StemKind.Vocals, StemKind.Guitar, StemKind.Piano], 44_100, 343_980);

    /// <summary>The same models with full-precision weights.</summary>
    public static StemModelInfo FourFull { get; } = Four with { FileName = "htdemucs-full.onnx", IsFull = true };

    public static StemModelInfo SixFull { get; } = Six with { FileName = "htdemucs_6s-full.onnx", IsFull = true };

    /// <summary>Every model, compact first.</summary>
    public static IReadOnlyList<StemModelInfo> All { get; } = [Four, Six, FourFull, SixFull];

    public static StemModelInfo For(StemMode mode, bool full = false) => (mode, full) switch
    {
        (StemMode.Six, true) => SixFull,
        (StemMode.Six, false) => Six,
        (_, true) => FourFull,
        _ => Four,
    };

    /// <summary>The stems of a mode in the order the window lists them.</summary>
    public static IReadOnlyList<StemKind> Shown(StemMode mode) => mode == StemMode.Six
        ? [StemKind.Vocals, StemKind.Drums, StemKind.Bass, StemKind.Guitar, StemKind.Piano, StemKind.Other]
        : [StemKind.Vocals, StemKind.Drums, StemKind.Bass, StemKind.Other];
}
