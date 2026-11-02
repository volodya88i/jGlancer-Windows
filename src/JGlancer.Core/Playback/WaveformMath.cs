namespace JGlancer.Core.Playback;

/// <summary>Peaks of decoded audio for the waveform scrubber (macOS WaveformService's reduction).</summary>
public static class WaveformMath
{
    /// <summary>Buckets across a whole file: about one per pixel of the widest scrubber.</summary>
    public const int Buckets = 2048;

    /// <summary>
    /// Per bucket the lowest and highest sample of 16-bit mono PCM, scaled to −1 … 1. Fewer samples than buckets give
    /// one bucket per sample.
    /// </summary>
    public static (float[] Lows, float[] Highs) Peaks(ReadOnlySpan<short> samples, int buckets = Buckets)
    {
        var count = Math.Min(buckets, samples.Length);
        var lows = new float[count];
        var highs = new float[count];
        for (var bucket = 0; bucket < count; bucket++)
        {
            var from = (int)((long)bucket * samples.Length / count);
            var to = (int)((long)(bucket + 1) * samples.Length / count);
            short low = 0;
            short high = 0;
            foreach (var sample in samples[from..to])
            {
                low = Math.Min(low, sample);
                high = Math.Max(high, sample);
            }
            lows[bucket] = low / 32768f;
            highs[bucket] = high / 32767f;
        }
        return (lows, highs);
    }

    /// <summary>
    /// The samples of a RIFF/WAVE file of 16-bit PCM: the "data" chunk, whose length may be unknown (0 or 0xFFFFFFFF
    /// in a header written before the end was known), then everything after it counts.
    /// </summary>
    public static short[] WaveSamples(ReadOnlySpan<byte> file)
    {
        if (file.Length < 12 || !file[..4].SequenceEqual("RIFF"u8) || !file[8..12].SequenceEqual("WAVE"u8))
        {
            return [];
        }
        var position = 12;
        while (position + 8 <= file.Length)
        {
            var id = file.Slice(position, 4);
            var length = BitConverter.ToUInt32(file.Slice(position + 4, 4));
            var start = position + 8;
            if (id.SequenceEqual("data"u8))
            {
                var available = file.Length - start;
                var bytes = length == 0 || length > available ? available : (int)length;
                bytes -= bytes % 2;
                var samples = new short[bytes / 2];
                System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(file.Slice(start, bytes)).CopyTo(samples);
                return samples;
            }
            position = start + (int)length + (int)(length % 2);
        }
        return [];
    }
}
