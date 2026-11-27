namespace JGlancer.Core.Stems;

/// <summary>
/// The 60 mel bands Mel-Band RoFormer splits the spectrum into (MAC-056): each band's first bin and number of bins.
/// They are librosa's Slaney mel filters for a 2048-point STFT at 44.1 kHz, a bin taken where its weight is above
/// zero, plus the first and the last bin; neighbouring bands overlap. <c>scripts/stems/convert_roformer.py</c> computes
/// the same table, and the checkpoint's layer sizes confirm the counts.
/// </summary>
public static class MelBands
{
    /// <summary>Each band's first bin and number of bins.</summary>
    public static IReadOnlyList<(int Start, int Count)> Bands { get; } =
    [
        (0, 7), (4, 6), (7, 6), (10, 6), (13, 6), (16, 6), (19, 6), (22, 6), (25, 6), (28, 6), (31, 6), (34, 6), (37, 6),
        (40, 6), (43, 6), (46, 7), (49, 7), (53, 7), (56, 9), (60, 9), (65, 9), (69, 10), (74, 10), (79, 11), (84, 13),
        (90, 13), (97, 13), (103, 15), (110, 16), (118, 17), (126, 19), (135, 20), (145, 20), (155, 22), (165, 24),
        (177, 26), (189, 28), (203, 29), (217, 31), (232, 33), (248, 36), (265, 39), (284, 41), (304, 44), (325, 47),
        (348, 50), (372, 54), (398, 57), (426, 61), (455, 66), (487, 71), (521, 76), (558, 80), (597, 86), (638, 93),
        (683, 99), (731, 105), (782, 113), (836, 122), (895, 130),
    ];

    /// <summary>The bins of all bands, band after band (1979: most bins are in two bands).</summary>
    public static int[] Bins { get; } = [.. Bands.SelectMany(band => Enumerable.Range(band.Start, band.Count))];

    /// <summary>The network's values per frame, in and out: for each band bin, left then right, real then imaginary.</summary>
    public static int Features { get; } = Bins.Length * 4;

    /// <summary>How many bands each of the 1025 bins is in: the masks of overlapping bands are averaged.</summary>
    public static float[] Coverage { get; } = MakeCoverage();

    private static float[] MakeCoverage()
    {
        var counts = new float[BandSpectrogram.Bins];
        foreach (var bin in Bins)
        {
            counts[bin]++;
        }
        return counts;
    }
}
