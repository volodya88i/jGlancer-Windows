using JGlancer.Core.Playback;

namespace JGlancer.Core.Tests.Playback;

public sealed class WaveformMathTests
{
    [Fact]
    public void PeaksKeepTheExtremesOfEachBucket()
    {
        short[] samples = [0, 16384, -8192, 0, 32767, -32768, 0, 0];
        var (lows, highs) = WaveformMath.Peaks(samples, buckets: 4);
        Assert.Equal([0.5f, 0, 1, 0], highs.Select(h => MathF.Round(h, 2)));
        Assert.Equal([0f, -0.25f, -1f, 0f], lows.Select(l => MathF.Round(l, 2)));
        Assert.Equal(2, WaveformMath.Peaks([1, 2], buckets: 10).Lows.Length);
    }

    [Fact]
    public void WaveFilesGiveTheirDataEvenWithAnUnknownLength()
    {
        static byte[] Wave(uint dataLength, short[] samples)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write("RIFF"u8);
            writer.Write(0u);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16u);
            writer.Write(new byte[16]);
            writer.Write("data"u8);
            writer.Write(dataLength);
            foreach (var sample in samples)
            {
                writer.Write(sample);
            }
            return stream.ToArray();
        }
        Assert.Equal([1, -2, 3], WaveformMath.WaveSamples(Wave(6, [1, -2, 3])));
        // ao_pcm writes the header first: an unknown length means "to the end of the file".
        Assert.Equal([1, -2, 3], WaveformMath.WaveSamples(Wave(0xFFFFFFFF, [1, -2, 3])));
        Assert.Empty(WaveformMath.WaveSamples("not a wave file"u8));
    }
}
