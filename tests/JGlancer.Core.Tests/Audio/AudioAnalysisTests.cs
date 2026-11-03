using JGlancer.Core.Audio;
using JGlancer.Core.Playback;
using JGlancer.Core.Store;

namespace JGlancer.Core.Tests.Audio;

public sealed class AudioAnalysisTests
{
    private const int Rate = 8000;

    /// <summary>Chords as MIDI notes, one second each, with some overtones, repeated.</summary>
    private static short[] Chords(int[][] chords, int repeats = 4)
    {
        var samples = new short[chords.Length * repeats * Rate];
        for (var i = 0; i < samples.Length; i++)
        {
            var notes = chords[i / Rate % chords.Length];
            var t = (double)i / Rate;
            var value = notes.Sum(note =>
            {
                var hz = 440 * Math.Pow(2, (note - 69) / 12.0);
                return Math.Sin(2 * Math.PI * hz * t) + 0.4 * Math.Sin(4 * Math.PI * hz * t) + 0.2 * Math.Sin(6 * Math.PI * hz * t);
            });
            samples[i] = (short)(value / notes.Length * 12000);
        }
        return samples;
    }

    private static int[][] Transposed(int[][] chords, int by) => [.. chords.Select(c => c.Select(n => n + by).ToArray())];

    private static readonly int[][] AMinor = [[57, 60, 64], [62, 65, 69], [64, 68, 71], [57, 60, 64]];
    private static readonly int[][] CMajor = [[60, 64, 67], [65, 69, 72], [67, 71, 74], [60, 64, 67]];

    [Fact]
    public void FindsTheKey()
    {
        Assert.Equal(new MusicKey(9, true), KeyDetector.Detect(Chords(AMinor), Rate));
        Assert.Equal(new MusicKey(0, false), KeyDetector.Detect(Chords(CMajor), Rate));
        Assert.Equal(new MusicKey(6, true), KeyDetector.Detect(Chords(Transposed(AMinor, -3)), Rate));
        Assert.Equal(new MusicKey(3, false), KeyDetector.Detect(Chords(Transposed(CMajor, 3)), Rate));
    }

    [Fact]
    public void SilenceHasNoKeyOrTempo()
    {
        Assert.Null(KeyDetector.Detect(new short[Rate * 10], Rate));
        Assert.Null(TempoDetector.Detect(new short[Rate * 10], Rate));
    }

    [Theory]
    [InlineData(90)]
    [InlineData(128)]
    [InlineData(150)]
    public void FindsTheTempo(double bpm)
    {
        // Short noise bursts on the beat.
        var random = new Random(7);
        var samples = new short[Rate * 40];
        var period = 60.0 * Rate / bpm;
        for (var beat = 0.0; beat < samples.Length; beat += period)
        {
            for (var i = 0; i < 400 && (int)beat + i < samples.Length; i++)
            {
                samples[(int)beat + i] = (short)((random.NextDouble() - 0.5) * 30000 * (1 - i / 400.0));
            }
        }
        var found = TempoDetector.Detect(samples, Rate);
        Assert.NotNull(found);
        Assert.InRange(found.Value, bpm - 1, bpm + 1);
    }

    [Fact]
    public void KeysShiftAndRead()
    {
        Assert.Equal("A min", new MusicKey(9, true).Label);
        Assert.Equal("B min", new MusicKey(9, true).Shifted(2).Label);
        Assert.Equal("Bb maj", new MusicKey(0, false).Shifted(-2).Label);
    }

    [Theory]
    [InlineData("5", 5)]
    [InlineData("+5", 5)]
    [InlineData(" -3 ", -3)]
    [InlineData("−3", -3)]
    [InlineData("1,5", 1.5)]
    [InlineData("1.5", 1.5)]
    [InlineData("99", 12)]
    [InlineData("-99", -12)]
    public void ReadsATypedPitch(string text, double semitones)
    {
        Assert.True(PlaybackTarget.TryParseSemitones(text, out var read));
        Assert.Equal(semitones, read);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData(null)]
    public void RefusesAnythingElse(string? text)
    {
        Assert.False(PlaybackTarget.TryParseSemitones(text, out _));
        Assert.False(PlaybackTarget.TryParseBpm(text, out _));
    }

    [Theory]
    [InlineData("140", 140)]
    [InlineData("128,5", 128.5)]
    public void ReadsATypedTempo(string text, double bpm)
    {
        Assert.True(PlaybackTarget.TryParseBpm(text, out var read));
        Assert.Equal(bpm, read);
    }

    [Fact]
    public void RefusesAnImpossibleTempo()
    {
        Assert.False(PlaybackTarget.TryParseBpm("0", out _));
        Assert.False(PlaybackTarget.TryParseBpm("-90", out _));
    }

    [Fact]
    public void TargetsSetExactRatesWithinAnOctave()
    {
        Assert.Equal(2, PlaybackTarget.RateForSemitones(12), 6);
        Assert.Equal(0.5, PlaybackTarget.RateForSemitones(-12), 6);
        Assert.Equal(1.122462, PlaybackTarget.RateForSemitones(2), 5);
        Assert.Equal(1.25, PlaybackTarget.RateForBpm(150, 120), 6);
        Assert.Equal(2, PlaybackTarget.RateForBpm(400, 100), 6);
        Assert.Equal(0.5, PlaybackTarget.RateForBpm(10, 100), 6);
        Assert.Equal(2, PlaybackTarget.Semitones(PlaybackTarget.RateForSemitones(2)), 6);
        Assert.True(PlaybackTarget.IsWhole(PlaybackTarget.Semitones(PlaybackTarget.RateForSemitones(-5))));
        Assert.False(PlaybackTarget.IsWhole(PlaybackTarget.Semitones(1.25)));
    }

    [Fact]
    public void OnlyAFasterRateWithKeepPitchGetsItsOwnFilter()
    {
        Assert.NotEmpty(PlaybackSpeed.StretchFilter(1.25, true));
        Assert.Empty(PlaybackSpeed.StretchFilter(1.25, false));
        Assert.Empty(PlaybackSpeed.StretchFilter(1, true));
        Assert.Empty(PlaybackSpeed.StretchFilter(0.8, true));
    }

    [Fact]
    public void TargetPitchReads()
    {
        Assert.Equal("+2", PlaybackTarget.Label(2));
        Assert.Equal("-3", PlaybackTarget.Label(-3));
        Assert.Equal("0", PlaybackTarget.Label(0));
        Assert.Equal("+1.6", PlaybackTarget.Label(1.55001));
    }

    [Fact]
    public async Task AnalysisHoldsForOneVersionOfTheFile()
    {
        using var store = LibraryStore.InMemory();
        var analysis = new AudioAnalysis(128, new MusicKey(9, true));
        await store.SaveAudioAnalysisAsync(@"C:\a\song.wav", 10, 20, analysis);
        Assert.Equal(analysis, await store.AudioAnalysisAsync(@"C:\a\song.wav", 10, 20));
        Assert.Null(await store.AudioAnalysisAsync(@"C:\a\song.wav", 10, 21));
        await store.SaveAudioAnalysisAsync(@"C:\a\song.wav", 10, 20, new AudioAnalysis(64, null));
        Assert.Equal(new AudioAnalysis(64, null), await store.AudioAnalysisAsync(@"C:\a\song.wav", 10, 20));
    }
}
