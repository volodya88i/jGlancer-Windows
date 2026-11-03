using System.Globalization;
using System.Numerics;
using JGlancer.Mpv;

namespace JGlancer.Tools;

/// <summary>
/// <c>keylab &lt;file:semitones&gt;…</c>: tries key detector variants on copies of songs shifted by known amounts. A good
/// detector follows the shift, so it needs no true key to compare with. Files sharing a name before <c>_</c> are one song.
/// </summary>
internal static class KeyLab
{
    private static readonly string[] Names = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"];

    private static readonly Dictionary<string, (double[] Major, double[] Minor)> Profiles = new()
    {
        ["kk"] = ([6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88], [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17]),
        ["temperley"] = ([5, 2, 3.5, 2, 4.5, 4, 2, 4.5, 2, 3.5, 1.5, 4], [5, 2, 3.5, 4.5, 2, 4, 2, 4.5, 3.5, 2, 1.5, 4]),
        ["shaath"] = ([6.6, 2, 3.5, 2.3, 4.6, 4, 2.5, 5.2, 2.4, 3.7, 2.3, 3.4], [6.5, 2.7, 3.5, 5.4, 2.6, 3.5, 2.5, 5.2, 4, 2.7, 4.3, 3.2]),
    };

    private static readonly bool[] Both = [false, true];
    private static readonly (double, double)[] Ranges = [(65.0, 2000.0), (55.0, 1000.0), (100.0, 2000.0), (32.0, 500.0)];
    private static readonly double[] Powers = [0.5, 1.0];

    private sealed record Variant(bool Peaks, bool Harmonic, bool Tuned, double Low, double High, double Power);

    public static int Run(IEnumerable<string> arguments)
    {
        var songs = new List<(string Song, int Shift, string Name, short[] Samples)>();
        foreach (var argument in arguments)
        {
            var split = argument.LastIndexOf(':');
            var path = argument[..split];
            var name = Path.GetFileNameWithoutExtension(path);
            var samples = MpvAudioDecoder.SamplesAsync(path, CancellationToken.None).GetAwaiter().GetResult() ?? [];
            songs.Add((name.Split('_')[0], int.Parse(argument[(split + 1)..], CultureInfo.InvariantCulture), name, samples));
        }
        var rate = MpvAudioDecoder.SamplesPerSecond;
        var variants = from peaks in Both
                       from harmonic in Both
                       from tuned in Both
                       from range in Ranges
                       from power in Powers
                       select new Variant(peaks, harmonic, tuned, range.Item1, range.Item2, power);
        var results = new List<(int Score, string Line)>();
        foreach (var variant in variants)
        {
            var chroma = songs.Select(s => Chroma(s.Samples, rate, variant)).ToList();
            foreach (var (profile, tables) in Profiles)
            {
                var score = 0;
                var relative = 0;
                var detail = new List<string>();
                foreach (var group in songs.Select((s, i) => (s, i)).GroupBy(p => p.s.Song))
                {
                    var keys = group.Select(p => (p.s.Shift, Key: Best(chroma[p.i], tables))).ToList();
                    var (bestScore, bestBase) = Enumerable.Range(0, 24).Select(k => (keys.Count(x => Same(k, x.Key, x.Shift)), k)).MaxBy(p => p.Item1);
                    score += bestScore;
                    relative += keys.Count(x => Same(bestBase, x.Key, x.Shift) || Same(Relative(bestBase), x.Key, x.Shift));
                    detail.Add($"{group.Key}={Label(bestBase)} {bestScore}/{keys.Count}");
                }
                results.Add((score * 100 + relative, $"{score,2} rel {relative,2} {profile,-9} peaks={variant.Peaks,-5} hpss={variant.Harmonic,-5} tune={variant.Tuned,-5} {variant.Low,3:0}-{variant.High,4:0}Hz pow={variant.Power:0.0} {string.Join(", ", detail)}"));
            }
        }
        foreach (var (_, line) in results.OrderByDescending(r => r.Score).Take(1000))
        {
            Console.WriteLine(line);
        }
        Console.WriteLine("...worst:");
        Console.WriteLine(results.MinBy(r => r.Score).Line);
        return 0;
    }

    private static bool Same(int key, int found, int shift) => found == Shifted(key, shift);

    private static int Shifted(int key, int shift) => (key % 12 + shift % 12 + 12) % 12 + (key >= 12 ? 12 : 0);

    /// <summary>Major (0–11) to its relative minor (12–23) and back.</summary>
    private static int Relative(int key) => key < 12 ? (key + 9) % 12 + 12 : (key - 12 + 3) % 12;

    private static string Label(int key) => $"{Names[key % 12]} {(key >= 12 ? "min" : "maj")}";

    private static int Best(double[] chroma, (double[] Major, double[] Minor) tables)
    {
        var best = 0;
        var top = double.NegativeInfinity;
        for (var key = 0; key < 24; key++)
        {
            var profile = key < 12 ? tables.Major : tables.Minor;
            var score = Correlation(chroma, profile, key % 12);
            if (score > top)
            {
                top = score;
                best = key;
            }
        }
        return best;
    }

    private static double Correlation(double[] chroma, double[] profile, int tonic)
    {
        var (mc, mp) = (chroma.Average(), profile.Average());
        double product = 0, cc = 0, pp = 0;
        for (var i = 0; i < 12; i++)
        {
            var c = chroma[(i + tonic) % 12] - mc;
            var p = profile[i] - mp;
            product += c * p;
            cc += c * c;
            pp += p * p;
        }
        return product / Math.Sqrt(cc * pp + double.Epsilon);
    }

    private static double[] Chroma(short[] samples, int rate, Variant variant)
    {
        const int window = 4096;
        var hop = variant.Harmonic ? 1024 : 2048;
        var frames = Math.Max(0, (samples.Length - window) / hop);
        var bins = window / 2 + 1;
        var spectrum = new float[frames][];
        var hann = Enumerable.Range(0, window).Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / window)).ToArray();
        Parallel.For(0, frames, frame =>
        {
            var buffer = new Complex[window];
            for (var i = 0; i < window; i++)
            {
                buffer[i] = samples[frame * hop + i] / 32768.0 * hann[i];
            }
            Fft(buffer);
            var row = new float[bins];
            for (var i = 0; i < bins; i++)
            {
                row[i] = (float)(buffer[i].Magnitude * 4 / window);
            }
            spectrum[frame] = row;
        });
        var first = Math.Max(1, (int)(variant.Low * window / rate));
        var last = Math.Min(bins - 2, (int)(variant.High * window / rate) + 1);
        if (variant.Harmonic)
        {
            spectrum = Harmonic(spectrum, first, last);
        }
        var offset = variant.Tuned ? Tuning(spectrum, first, last, rate, window) : 0;
        var total = new double[12];
        var frameChroma = new double[12];
        foreach (var row in spectrum)
        {
            Array.Clear(frameChroma);
            var loudest = 0f;
            for (var bin = first; bin <= last; bin++)
            {
                var value = row[bin];
                loudest = Math.Max(loudest, value);
                var position = (double)bin;
                if (variant.Peaks)
                {
                    if (value <= row[bin - 1] || value < row[bin + 1])
                    {
                        continue;
                    }
                    position = bin + Interpolate(row[bin - 1], value, row[bin + 1]);
                }
                var midi = 69 + 12 * Math.Log2(position * rate / window / 440) - offset;
                frameChroma[((int)Math.Round(midi) % 12 + 12) % 12] += Math.Pow(value, variant.Power);
            }
            var sum = frameChroma.Sum();
            if (loudest < 0.002f || sum <= 0)
            {
                continue;
            }
            for (var i = 0; i < 12; i++)
            {
                total[i] += frameChroma[i] / sum;
            }
        }
        return total;
    }

    private static double Interpolate(float a, float b, float c)
    {
        var curve = a - 2 * b + c;
        return Math.Abs(curve) < 1e-9 ? 0 : 0.5 * (a - c) / curve;
    }

    /// <summary>The song's tuning in semitones from A = 440: the most common offset of spectral peaks from the nearest note.</summary>
    private static double Tuning(float[][] spectrum, int first, int last, int rate, int window)
    {
        var histogram = new double[50];
        foreach (var row in spectrum)
        {
            for (var bin = first; bin <= last; bin++)
            {
                if (row[bin] > row[bin - 1] && row[bin] >= row[bin + 1] && row[bin] > 0.002f)
                {
                    var midi = 69 + 12 * Math.Log2((bin + Interpolate(row[bin - 1], row[bin], row[bin + 1])) * rate / window / 440);
                    var off = midi - Math.Round(midi);
                    histogram[Math.Clamp((int)((off + 0.5) * 50), 0, 49)] += Math.Sqrt(row[bin]);
                }
            }
        }
        return (Array.IndexOf(histogram, histogram.Max()) + 0.5) / 50 - 0.5;
    }

    /// <summary>Keeps what holds steady in time and drops what spreads across frequency (drums): median filtering.</summary>
    private static float[][] Harmonic(float[][] spectrum, int first, int last)
    {
        const int radius = 8;
        var output = new float[spectrum.Length][];
        Parallel.For(0, spectrum.Length, frame =>
        {
            var row = (float[])spectrum[frame].Clone();
            var time = new float[2 * radius + 1];
            var freq = new float[2 * radius + 1];
            for (var bin = first; bin <= last; bin++)
            {
                for (var k = -radius; k <= radius; k++)
                {
                    time[k + radius] = spectrum[Math.Clamp(frame + k, 0, spectrum.Length - 1)][bin];
                    freq[k + radius] = spectrum[frame][Math.Clamp(bin + k, 0, row.Length - 1)];
                }
                Array.Sort(time);
                Array.Sort(freq);
                if (time[radius] < freq[radius])
                {
                    row[bin] = 0;
                }
            }
            output[frame] = row;
        });
        return output;
    }

    private static void Fft(Complex[] a)
    {
        var n = a.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }
            j ^= bit;
            if (i < j)
            {
                (a[i], a[j]) = (a[j], a[i]);
            }
        }
        for (var length = 2; length <= n; length <<= 1)
        {
            var step = Complex.FromPolarCoordinates(1, -2 * Math.PI / length);
            for (var start = 0; start < n; start += length)
            {
                var w = Complex.One;
                for (var k = 0; k < length / 2; k++)
                {
                    var (x, y) = (a[start + k], a[start + k + length / 2] * w);
                    a[start + k] = x + y;
                    a[start + k + length / 2] = x - y;
                    w *= step;
                }
            }
        }
    }
}
