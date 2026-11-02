using JGlancer.Core.Playback;
using JGlancer.Core.Stems;

namespace JGlancer.Core.Tests.Stems;

public sealed class StemTests
{
    /// <summary>Every source is the piece itself: joining the pieces must give the song back.</summary>
    private sealed class Identity(int sources, int segment) : IStemSeparator
    {
        public int Sources => sources;

        public int SegmentFrames => segment;

        public int Runs { get; private set; }

        public void Separate(ReadOnlySpan<float> input, Span<float> output)
        {
            Runs++;
            for (var source = 0; source < sources; source++)
            {
                input.CopyTo(output.Slice(source * input.Length, input.Length));
            }
        }
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

    private static (float[] Left, float[] Right) Song(int frames)
    {
        var left = new float[frames];
        var right = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            left[i] = 0.5f * MathF.Sin(i * 0.031f) + 0.1f;
            right[i] = 0.3f * MathF.Cos(i * 0.0071f) - 0.2f * MathF.Sin(i * 0.17f);
        }
        return (left, right);
    }

    [Fact]
    public void PiecesOverlapByAQuarterAndCoverTheSong()
    {
        var plan = StemSplitter.Plan(2500, 1000);
        Assert.Equal([new StemSegment(0, 1000), new StemSegment(750, 1000), new StemSegment(1500, 1000), new StemSegment(2250, 250)], plan);
        Assert.Equal([new StemSegment(0, 300)], StemSplitter.Plan(300, 1000));
        var weights = StemSplitter.Weights(8);
        Assert.Equal([0.25f, 0.5f, 0.75f, 1f, 1f, 0.75f, 0.5f, 0.25f], weights);
    }

    [Theory]
    [InlineData(4321, 1000)]
    [InlineData(300, 1000)]
    [InlineData(1000, 1000)]
    public void JoiningThePiecesOfAnIdentityGivesTheSongBack(int frames, int segment)
    {
        var (left, right) = Song(frames);
        var separator = new Identity(4, segment);
        var vocals = new Collect();
        var bass = new Collect();
        var progress = new List<double>();
        StemSplitter.Split(left, right, separator, new Dictionary<int, IStemSink> { [3] = vocals, [1] = bass }, progress.Add, CancellationToken.None);
        Assert.Equal(frames, vocals.Left.Count);
        Assert.Equal(frames, bass.Right.Count);
        for (var i = 0; i < frames; i++)
        {
            Assert.Equal(left[i], vocals.Left[i], 1e-5f);
            Assert.Equal(right[i], vocals.Right[i], 1e-5f);
            Assert.Equal(right[i], bass.Right[i], 1e-5f);
        }
        Assert.Equal(1, progress[^1]);
        Assert.Equal(separator.Runs, progress.Count);
    }

    [Fact]
    public void StoppingEndsBetweenPieces()
    {
        var (left, right) = Song(5000);
        using var cancel = new CancellationTokenSource();
        var separator = new Identity(4, 1000);
        Assert.Throws<OperationCanceledException>(() => StemSplitter.Split(left, right, separator,
            new Dictionary<int, IStemSink> { [0] = new Collect() }, _ => cancel.Cancel(), cancel.Token));
        Assert.Equal(1, separator.Runs);
    }

    [Fact]
    public void TheSpectrogramIsPyTorchs()
    {
        // Values from scripts/stems (HTDemucs._spec + _magnitude of this signal).
        const int length = 343_980;
        var mix = new float[2 * length];
        for (var i = 0; i < length; i++)
        {
            var t = i / 44100.0;
            mix[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * t) + 0.25 * Math.Sin(2 * Math.PI * 3000 * t + 1));
            mix[length + i] = (float)(0.4 * Math.Sin(2 * Math.PI * 97 * t) + 0.1 * Math.Cos(2 * Math.PI * 12345 * t));
        }
        var frames = Spectrogram.Frames(length);
        Assert.Equal(336, frames);
        var spec = new float[4 * Spectrogram.Bins * frames];
        Spectrogram.Forward(mix, length, spec);
        (int Channel, int Bin, int Frame, float Value)[] expected =
        [
            (0, 0, 0, 0.2209745f), (0, 40, 0, 5.112086f), (1, 40, 5, -1.70224f), (2, 9, 100, -4.937966f),
            (3, 9, 100, -4.070854f), (0, 278, 335, -0.1851368f), (1, 278, 200, -2.963294f),
            (2, 1146, 17, -0.9236326f), (3, 2047, 335, 0.0009935535f), (2, 2, 1, 0.02341787f),
        ];
        foreach (var (channel, bin, frame, value) in expected)
        {
            Assert.Equal(value, spec[(channel * Spectrogram.Bins + bin) * frames + frame], 2e-3f);
        }
        // The inverse of the spectrogram is the piece again (one "source" that is the mix), except near its ends:
        // Demucs leaves two frames empty there, which the overlap of the pieces hides.
        var back = new float[2 * length];
        Spectrogram.InverseAdd(spec, 1, length, back);
        for (var channel = 0; channel < 2; channel++)
        {
            for (var i = Spectrogram.Size; i < length - Spectrogram.Size; i += 997)
            {
                Assert.Equal(mix[channel * length + i], back[channel * length + i], 1e-3f);
            }
        }
    }

    [Fact]
    public void FoldersAndFilesAreNamedAfterTheSong()
    {
        var taken = new HashSet<string> { Path.Combine("C:\\Music", "Lekarstvo split"), Path.Combine("C:\\Music", "Lekarstvo split 2") };
        Assert.Equal(Path.Combine("C:\\Music", "Lekarstvo split 3"), StemNaming.DefaultFolder("C:\\Music\\Lekarstvo.mp3", taken.Contains));
        Assert.Equal(Path.Combine("C:\\Music", "My.Song split"), StemNaming.DefaultFolder("C:\\Music\\My.Song.flac", _ => false));
        Assert.Equal("Lekarstvo - Vocals.wav", StemNaming.FileName("C:\\Music\\Lekarstvo.mp3", StemKind.Vocals));
        Assert.Equal("a_b - Drums.wav", StemNaming.FileName("C:\\a_b.wav", StemKind.Drums));
        Assert.Equal("CON_", StemNaming.Safe("CON?"));
        Assert.Equal("_CON", StemNaming.Safe("CON"));
        Assert.Equal("x", StemNaming.Safe("x. "));
    }

    [Fact]
    public void AtLeastOneStemStaysTickedAndModesShareTheirTicks()
    {
        var choice = StemChoice.Default;
        Assert.Equal([StemKind.Vocals, StemKind.Drums, StemKind.Bass, StemKind.Other], choice.Stems);
        choice = choice.With(StemKind.Drums, false).With(StemKind.Bass, false).With(StemKind.Other, false);
        Assert.Equal([StemKind.Vocals], choice.Stems);
        Assert.Same(choice, choice.With(StemKind.Vocals, false));
        Assert.True(choice.CanSplit);
        var six = choice.WithMode(StemMode.Six);
        Assert.Equal([StemKind.Vocals, StemKind.Guitar, StemKind.Piano], six.Stems);
        var four = six.With(StemKind.Vocals, false).WithMode(StemMode.Four);
        Assert.Equal([StemKind.Vocals, StemKind.Drums, StemKind.Bass, StemKind.Other], four.Stems);
    }

    [Fact]
    public void AStemIsA24BitWaveOfWhatWasWritten()
    {
        using var memory = new MemoryStream();
        var writer = new WaveStemWriter(memory, 44_100);
        writer.Write([0f, 0.5f, -1f, 2f], [1f, -0.25f, 0f, -2f]);
        writer.Write([0.125f], [0f]);
        writer.Complete();
        var audio = PcmFiles.ReadWave(memory.ToArray());
        Assert.NotNull(audio);
        Assert.Equal((44_100, 2, 24, false, 5L), (audio.SampleRate, audio.Channels, audio.BitsPerSample, audio.IsFloat, audio.Frames));
        static float Sample(byte[] data, int index) => ((data[index * 3] | (data[index * 3 + 1] << 8) | (data[index * 3 + 2] << 16)) << 8 >> 8) / 8_388_607f;
        Assert.Equal(0.5f, Sample(audio.Data, 2), 1e-6f);
        Assert.Equal(-1f, Sample(audio.Data, 4), 1e-6f);
        Assert.Equal(1f, Sample(audio.Data, 6), 1e-6f);
        Assert.Equal(-1f, Sample(audio.Data, 7), 1e-6f);
        Assert.Equal(0.125f, Sample(audio.Data, 8), 1e-6f);
    }
}
