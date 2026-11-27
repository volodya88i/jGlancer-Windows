using JGlancer.Core.Stems;

namespace JGlancer.Core.Tests.Stems;

public sealed class RoFormerTests
{
    private static float[] Tone(int length, double phase = 0)
    {
        var signal = new float[length];
        for (var i = 0; i < length; i++)
        {
            var t = i / 44100.0;
            signal[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * t + phase) + 0.25 * Math.Sin(2 * Math.PI * 3000 * t + 1));
        }
        return signal;
    }

    [Fact]
    public void TheSpectrogramIsPyTorchs()
    {
        // torch.stft(x, 2048, 441, 2048, torch.hann_window(2048), return_complex=True) of this tone.
        var signal = Tone(RoFormerSeparator.Segment);
        var frames = BandSpectrogram.Frames(signal.Length);
        Assert.Equal(801, frames);
        var real = new float[frames * BandSpectrogram.Bins];
        var imaginary = new float[real.Length];
        new BandSpectrogram().Forward(signal, real, imaginary);
        (int Frame, int Bin, float Real, float Imaginary)[] expected =
        [
            (0, 0, 16.59197f, 0f), (0, 20, 101.1425f, 0f), (5, 20, 0f, -226.4349f), (400, 139, -100.7922f, 64.71787f),
            (800, 1024, -0.06125164f, 0f), (17, 500, 0f, 0f), (800, 20, -115.2884f, -6.73f), (3, 21, -197.0842f, 64.03584f),
        ];
        foreach (var (frame, bin, re, im) in expected)
        {
            Assert.Equal(re, real[frame * BandSpectrogram.Bins + bin], 2e-2f);
            Assert.Equal(im, imaginary[frame * BandSpectrogram.Bins + bin], 2e-2f);
        }
    }

    [Fact]
    public void TheInverseGivesTheSignalBack()
    {
        var signal = Tone(30_000);
        var spectrogram = new BandSpectrogram();
        var real = new float[BandSpectrogram.Frames(signal.Length) * BandSpectrogram.Bins];
        var imaginary = new float[real.Length];
        spectrogram.Forward(signal, real, imaginary);
        var back = new float[signal.Length];
        spectrogram.Inverse(real, imaginary, back);
        for (var i = 0; i < signal.Length; i++)
        {
            Assert.Equal(signal[i], back[i], 1e-4f);
        }
    }

    [Fact]
    public void TheBandsAreTheModelsSixty()
    {
        Assert.Equal(60, MelBands.Bands.Count);
        Assert.Equal(1979, MelBands.Bins.Length);
        Assert.Equal(7916, MelBands.Features);
        Assert.Equal(1024, MelBands.Bands[^1].Start + MelBands.Bands[^1].Count - 1);
        // Every bin is in a band, most in two.
        Assert.All(MelBands.Coverage, count => Assert.InRange(count, 1, 2));
    }

    /// <summary>A mask of 1: the vocals are the whole piece.</summary>
    private sealed class Pass : IBandMaskNetwork
    {
        public int Runs { get; private set; }

        public void Run(float[] bands, Span<float> masks)
        {
            Runs++;
            Assert.Equal(801 * MelBands.Features, bands.Length);
            for (var i = 0; i < masks.Length; i += 2)
            {
                masks[i] = 1;
                masks[i + 1] = 0;
            }
        }
    }

    [Fact]
    public void AMaskOfOneKeepsTheSongAndHalfOverlapsCoverIt()
    {
        var length = 500_000;
        var left = Tone(length);
        var right = Tone(length, phase: 1);
        var network = new Pass();
        var separator = new RoFormerSeparator(network);
        Assert.Equal((0.5, false), (separator.Overlap, separator.Normalizes));
        var vocals = new Collect();
        var model = StemModelInfo.For(StemNetwork.Vocals);
        StemSplitter.Split(left, right, separator, [new(model.Part(StemKind.Vocals)!.Value, vocals)], null, CancellationToken.None);
        // 0, 4 s, 8 s: pieces every half segment.
        Assert.Equal(StemSplitter.Plan(length, RoFormerSeparator.Segment, 0.5).Count, network.Runs);
        Assert.Equal(3, network.Runs);
        Assert.Equal(length, vocals.Left.Count);
        for (var i = 0; i < length; i += 101)
        {
            Assert.Equal(left[i], vocals.Left[i], 1e-3f);
            Assert.Equal(right[i], vocals.Right[i], 1e-3f);
        }
    }

    [Fact]
    public void HighQualityIsForTwoStemsOnly()
    {
        var two = new StemChoice(StemMode.Two, new HashSet<StemKind> { StemKind.Vocals, StemKind.Instrumental }, HighQuality: true);
        Assert.Equal(StemNetwork.Vocals, two.Network);
        Assert.Equal(StemNetwork.Four, (two with { HighQuality = false }).Network);
        var four = two.WithMode(StemMode.Four);
        Assert.True(four.HighQuality);
        Assert.Equal(StemNetwork.Four, four.Network);
        Assert.Equal(StemModelInfo.RoFormerFull, StemModelInfo.For(two.Network, full: true));
        Assert.Equal(new StemPart(0, SongWithout: true), StemModelInfo.RoFormer.Part(StemKind.Instrumental));
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
