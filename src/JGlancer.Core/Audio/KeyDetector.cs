namespace JGlancer.Core.Audio;

/// <summary>
/// Finds a song's key (W-107): how much of each of the 12 pitch classes its spectral peaks hold over its whole length,
/// compared with Sha'ath's profiles of all 24 keys (tuned to popular and electronic music). Peaks at their interpolated
/// frequency leave out most of the noise and drums. Takes the 8 kHz mono samples the waveform also uses.
/// </summary>
public static class KeyDetector
{
    private const int Window = 4096;
    private const int Hop = 2048;
    private const double LowestHz = 65;
    private const double HighestHz = 2000;
    private const float SilentFrame = 0.002f;

    private static readonly double[] MajorProfile = [6.6, 2, 3.5, 2.3, 4.6, 4, 2.5, 5.2, 2.4, 3.7, 2.3, 3.4];
    private static readonly double[] MinorProfile = [6.5, 2.7, 3.5, 5.4, 2.6, 3.5, 2.5, 5.2, 4, 2.7, 4.3, 3.2];

    /// <summary>The key, or null when the samples are too short or silent.</summary>
    public static MusicKey? Detect(ReadOnlySpan<short> samples, int sampleRate)
    {
        var chroma = Chroma(samples, sampleRate);
        if (chroma is null)
        {
            return null;
        }
        var best = new MusicKey(0, false);
        var bestScore = double.NegativeInfinity;
        for (var tonic = 0; tonic < 12; tonic++)
        {
            foreach (var minor in (ReadOnlySpan<bool>)[false, true])
            {
                var score = Correlation(chroma, minor ? MinorProfile : MajorProfile, tonic);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = new MusicKey(tonic, minor);
                }
            }
        }
        return best;
    }

    private static double[]? Chroma(ReadOnlySpan<short> samples, int sampleRate)
    {
        if (samples.Length < Window)
        {
            return null;
        }
        var fft = new AudioFft(Window);
        var first = Math.Max(1, (int)(LowestHz * Window / sampleRate));
        var last = Math.Min(Window / 2 - 1, (int)(HighestHz * Window / sampleRate) + 1);
        var magnitudes = new float[Window / 2 + 1];
        var frame = new double[12];
        var total = new double[12];
        var heard = false;
        for (var start = 0; start + Window <= samples.Length; start += Hop)
        {
            fft.Magnitudes(samples, start, magnitudes);
            Array.Clear(frame);
            var loudest = 0f;
            for (var bin = first; bin <= last; bin++)
            {
                var (below, value, above) = (magnitudes[bin - 1], magnitudes[bin], magnitudes[bin + 1]);
                loudest = Math.Max(loudest, value);
                if (value > below && value >= above)
                {
                    var curve = below - 2 * value + above;
                    var hz = (bin + (curve == 0 ? 0 : 0.5 * (below - above) / curve)) * sampleRate / Window;
                    var note = (int)Math.Round(12 * Math.Log2(hz / 440)) + 9;
                    frame[(note % 12 + 12) % 12] += value;
                }
            }
            if (loudest < SilentFrame)
            {
                continue;
            }
            // Each moment counts the same: a loud chorus must not decide alone.
            var sum = frame.Sum();
            if (sum <= 0)
            {
                continue;
            }
            for (var i = 0; i < 12; i++)
            {
                total[i] += frame[i] / sum;
            }
            heard = true;
        }
        return heard ? total : null;
    }

    private static double Correlation(double[] chroma, double[] profile, int tonic)
    {
        var meanChroma = chroma.Average();
        var meanProfile = profile.Average();
        double product = 0, chromaSquares = 0, profileSquares = 0;
        for (var i = 0; i < 12; i++)
        {
            var c = chroma[(i + tonic) % 12] - meanChroma;
            var p = profile[i] - meanProfile;
            product += c * p;
            chromaSquares += c * c;
            profileSquares += p * p;
        }
        return product / Math.Sqrt(chromaSquares * profileSquares + double.Epsilon);
    }
}
