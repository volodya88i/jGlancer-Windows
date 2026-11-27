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

    /// <summary>Everything but the vocals (2 stems): the song less the vocals, so the two add up to the song (MAC-055).</summary>
    Instrumental,
}

/// <summary>
/// The network a split runs: Demucs with 4 sources or 6, or Mel-Band RoFormer with the vocals alone (2 stems at High
/// Quality, MAC-056).
/// </summary>
public enum StemNetwork
{
    Four,
    Six,
    Vocals,
}

/// <summary>
/// What the split window offers: 2 stems (vocals and the instrumental, from the 4-stem network, macOS D-130), 4 (vocals,
/// drums, bass, the rest) or 6 (also guitar and piano).
/// </summary>
public enum StemMode
{
    Two,
    Four,
    Six,
}

/// <summary>What a mode runs.</summary>
public static class StemModes
{
    /// <summary>
    /// The Demucs network of <paramref name="mode"/>: for 2 stems the 4-stem one, which separates vocals better than the
    /// 6-stem one (<see cref="StemChoice.Network"/> may take RoFormer instead).
    /// </summary>
    public static StemNetwork Network(this StemMode mode) => mode == StemMode.Six ? StemNetwork.Six : StemNetwork.Four;
}

/// <summary>
/// One model as the app uses it: its file, the order of the sources it produces (the network's own order), its sample
/// rate and the length of the piece of audio it takes at once. <paramref name="IsFull"/>: the weights at full precision
/// (twice the download); otherwise compact, half-precision weights computing at full precision (W-92).
/// </summary>
public sealed record StemModelInfo(StemNetwork Network, string FileName, IReadOnlyList<StemKind> Sources, int SampleRate, int SegmentFrames, bool IsFull = false)
{
    /// <summary>Demucs v4 Hybrid Transformer, 4 sources (htdemucs).</summary>
    public static StemModelInfo Four { get; } = new(StemNetwork.Four, "htdemucs.onnx",
        [StemKind.Drums, StemKind.Bass, StemKind.Other, StemKind.Vocals], 44_100, 343_980);

    /// <summary>Demucs v4 Hybrid Transformer, 6 sources (htdemucs_6s).</summary>
    public static StemModelInfo Six { get; } = new(StemNetwork.Six, "htdemucs_6s.onnx",
        [StemKind.Drums, StemKind.Bass, StemKind.Other, StemKind.Vocals, StemKind.Guitar, StemKind.Piano], 44_100, 343_980);

    /// <summary>The same models with full-precision weights.</summary>
    public static StemModelInfo FourFull { get; } = Four with { FileName = "htdemucs-full.onnx", IsFull = true };

    public static StemModelInfo SixFull { get; } = Six with { FileName = "htdemucs_6s-full.onnx", IsFull = true };

    /// <summary>Mel-Band RoFormer, the vocals (Kimberley Jensen's model, MIT; MAC-056).</summary>
    public static StemModelInfo RoFormer { get; } = new(StemNetwork.Vocals, "mel_band_roformer.onnx", [StemKind.Vocals], 44_100, RoFormerSeparator.Segment);

    public static StemModelInfo RoFormerFull { get; } = RoFormer with { FileName = "mel_band_roformer-full.onnx", IsFull = true };

    /// <summary>Every model, Demucs first, compact before full.</summary>
    public static IReadOnlyList<StemModelInfo> All { get; } = [Four, Six, FourFull, SixFull, RoFormer, RoFormerFull];

    public static StemModelInfo For(StemNetwork network, bool full = false) => (network, full) switch
    {
        (StemNetwork.Vocals, true) => RoFormerFull,
        (StemNetwork.Vocals, false) => RoFormer,
        (StemNetwork.Six, true) => SixFull,
        (StemNetwork.Six, false) => Six,
        (_, true) => FourFull,
        _ => Four,
    };

    /// <summary>The stems of a mode in the order the window lists them.</summary>
    public static IReadOnlyList<StemKind> Shown(StemMode mode) => mode switch
    {
        StemMode.Two => [StemKind.Vocals, StemKind.Instrumental],
        StemMode.Six => [StemKind.Vocals, StemKind.Drums, StemKind.Bass, StemKind.Guitar, StemKind.Piano, StemKind.Other],
        _ => [StemKind.Vocals, StemKind.Drums, StemKind.Bass, StemKind.Other],
    };

    /// <summary>
    /// How this network makes <paramref name="stem"/>: one of its sources, or for the instrumental the song less the
    /// vocals; null when it has neither.
    /// </summary>
    public StemPart? Part(StemKind stem)
    {
        var source = Sources.ToList().IndexOf(stem == StemKind.Instrumental ? StemKind.Vocals : stem);
        return source < 0 ? null : new StemPart(source, SongWithout: stem == StemKind.Instrumental);
    }
}
