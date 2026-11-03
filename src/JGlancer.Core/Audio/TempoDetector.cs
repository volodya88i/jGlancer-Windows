namespace JGlancer.Core.Audio;

/// <summary>
/// Finds a song's tempo (W-107): the strength of note onsets over time, then the beat period that repeats best in it,
/// leaning toward moderate tempos; half or double the true tempo remains possible, the player offers ×2 and ÷2.
/// Takes the 8 kHz mono samples the waveform also uses.
/// </summary>
public static class TempoDetector
{
    /// <summary>How far ×2 and ÷2 may take a tempo.</summary>
    public const double ShownSlowest = 30;

    /// <summary>How far ×2 and ÷2 may take a tempo.</summary>
    public const double ShownFastest = 400;

    private const int Window = 512;
    private const double EnvelopeRate = 100;
    private const double SlowestBpm = 60;
    private const double FastestBpm = 200;
    private const double PreferredBpm = 120;
    private const double PreferenceOctaves = 1.2;
    private const int LongestEnvelope = 18000;
    private const int RefinedBeats = 8;
    private const float Compression = 100f;

    /// <summary>Beats per minute, or null when the samples are too short or have no onsets.</summary>
    public static double? Detect(ReadOnlySpan<short> samples, int sampleRate)
    {
        var envelope = Envelope(samples, sampleRate);
        var minLag = (int)Math.Floor(60 * EnvelopeRate / FastestBpm);
        var maxLag = (int)Math.Ceiling(60 * EnvelopeRate / SlowestBpm);
        if (envelope.Length < maxLag * 4)
        {
            return null;
        }
        var correlation = new double[Math.Min(maxLag * RefinedBeats + 2, envelope.Length)];
        for (var lag = minLag; lag < correlation.Length; lag++)
        {
            double sum = 0;
            for (var i = lag; i < envelope.Length; i++)
            {
                sum += envelope[i] * envelope[i - lag];
            }
            correlation[lag] = sum / (envelope.Length - lag);
        }
        var bestLag = 0;
        var bestScore = 0.0;
        for (var lag = minLag; lag <= maxLag; lag++)
        {
            // A real beat also repeats at two and four beats.
            var score = correlation[lag] + 0.5 * At(correlation, lag * 2) + 0.25 * At(correlation, lag * 4);
            var octaves = Math.Log2(60 * EnvelopeRate / lag / PreferredBpm) / PreferenceOctaves;
            score *= Math.Exp(-0.5 * octaves * octaves);
            if (score > bestScore)
            {
                bestScore = score;
                bestLag = lag;
            }
        }
        if (bestLag == 0)
        {
            return null;
        }
        return Math.Clamp(60 * EnvelopeRate / Refined(correlation, bestLag), SlowestBpm, FastestBpm);
    }

    private static double At(double[] values, int index) => index < values.Length ? values[index] : 0;

    /// <summary>The period to a fraction of an envelope step: measured over several beats, the error divides by their number.</summary>
    private static double Refined(double[] correlation, int lag)
    {
        var period = (double)lag;
        for (var beats = 2; beats <= RefinedBeats && (beats + 1) * lag < correlation.Length; beats *= 2)
        {
            var centre = period * beats;
            var from = (int)Math.Floor(centre - beats);
            var to = (int)Math.Ceiling(centre + beats);
            var peak = from;
            for (var i = from; i <= to && i < correlation.Length - 1; i++)
            {
                if (correlation[i] > correlation[peak])
                {
                    peak = i;
                }
            }
            var (a, b, c) = (correlation[peak - 1], correlation[peak], correlation[peak + 1]);
            var curve = a - 2 * b + c;
            var offset = Math.Abs(curve) < 1e-12 ? 0 : 0.5 * (a - c) / curve;
            period = (peak + Math.Clamp(offset, -0.5, 0.5)) / beats;
        }
        return period;
    }

    /// <summary>The rise of compressed spectral energy per step, its local average removed, from the song's middle when it is long.</summary>
    private static float[] Envelope(ReadOnlySpan<short> samples, int sampleRate)
    {
        var hop = (int)Math.Round(sampleRate / EnvelopeRate);
        if (samples.Length < Window + hop)
        {
            return [];
        }
        var frames = (samples.Length - Window) / hop;
        var skip = Math.Max(0, frames - LongestEnvelope) / 2;
        frames = Math.Min(frames, LongestEnvelope);
        var fft = new AudioFft(Window);
        var current = new float[Window / 2 + 1];
        var previous = new float[Window / 2 + 1];
        var flux = new float[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            fft.Magnitudes(samples, (frame + skip) * hop, current);
            var rise = 0f;
            for (var bin = 0; bin < current.Length; bin++)
            {
                var value = MathF.Log(1 + Compression * current[bin]);
                if (frame > 0 && value > previous[bin])
                {
                    rise += value - previous[bin];
                }
                previous[bin] = value;
            }
            flux[frame] = rise;
        }
        var radius = (int)EnvelopeRate / 2;
        var envelope = new float[frames];
        double running = 0;
        for (var i = 0; i < frames; i++)
        {
            running += flux[i];
            if (i > 2 * radius)
            {
                running -= flux[i - 2 * radius - 1];
            }
            var mean = running / Math.Min(i + 1, 2 * radius + 1);
            var at = Math.Max(0, i - radius);
            envelope[at] = Math.Max(0, flux[at] - (float)mean);
        }
        return envelope;
    }
}
