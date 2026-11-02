namespace JGlancer.Core.Stems;

/// <summary>
/// The spectrogram HTDemucs works on (W-85), computed here because STFT/iSTFT do not export to ONNX: PyTorch's
/// <c>stft</c>/<c>istft</c> with a 4096-point periodic Hann window, hop 1024, normalized, centred with reflection,
/// plus Demucs' own padding (<c>HTDemucs._spec</c> / <c>_ispec</c>), so the network sees exactly what it was trained on.
/// Layout: channels (left re, left im, right re, right im) × 2048 bins × frames, the highest bin dropped.
/// </summary>
public static class Spectrogram
{
    public const int Size = 4096;

    public const int Hop = Size / 4;

    public const int Bins = Size / 2;

    /// <summary>Demucs pads the signal by 1.5 hops each side before the STFT.</summary>
    private const int Pad = Hop / 2 * 3;

    private static readonly Fft Transform = new(Size);

    private static readonly float[] Window = MakeWindow();

    /// <summary>Frames of a piece of <paramref name="length"/> samples.</summary>
    public static int Frames(int length) => (length + Hop - 1) / Hop;

    /// <summary>
    /// The spectrogram of a stereo piece (planar: <paramref name="length"/> left samples, then right) into
    /// <paramref name="spec"/> (4 × <see cref="Bins"/> × <see cref="Frames"/>).
    /// </summary>
    public static void Forward(ReadOnlySpan<float> mix, int length, Span<float> spec)
    {
        var frames = Frames(length);
        var padded = length + Pad + Pad + frames * Hop - length;
        var re = new float[Size];
        var im = new float[Size];
        var scale = 1 / MathF.Sqrt(Size);
        for (var channel = 0; channel < 2; channel++)
        {
            var signal = mix.Slice(channel * length, length);
            var reBase = (channel * 2) * Bins * frames;
            var imBase = reBase + Bins * frames;
            for (var frame = 0; frame < frames; frame++)
            {
                // torch.stft centres frames by reflecting n_fft/2 samples; Demucs keeps frames 2 … 2 + frames.
                var start = (frame + 2) * Hop - Size / 2;
                for (var n = 0; n < Size; n++)
                {
                    var p = Reflect(start + n, padded);
                    re[n] = signal[Reflect(p - Pad, length)] * Window[n];
                    im[n] = 0;
                }
                Transform.Run(re, im, inverse: false);
                for (var bin = 0; bin < Bins; bin++)
                {
                    spec[reBase + bin * frames + frame] = re[bin] * scale;
                    spec[imBase + bin * frames + frame] = im[bin] * scale;
                }
            }
        }
    }

    /// <summary>
    /// Inverts the sources' spectrograms (<paramref name="sources"/> × 4 × <see cref="Bins"/> × frames) and adds the
    /// waveforms to <paramref name="output"/> (sources × 2 × <paramref name="length"/>).
    /// </summary>
    public static void InverseAdd(ReadOnlySpan<float> spec, int sources, int length, Span<float> output)
    {
        var frames = Frames(length);
        // Demucs adds two empty frames each side; torch.istft then drops n_fft/2 samples and Demucs the padding.
        var allFrames = frames + 4;
        var total = Size + Hop * (allFrames - 1);
        var signal = new float[total];
        var envelope = new float[total];
        for (var frame = 0; frame < allFrames; frame++)
        {
            for (var n = 0; n < Size; n++)
            {
                envelope[frame * Hop + n] += Window[n] * Window[n];
            }
        }
        var re = new float[Size];
        var im = new float[Size];
        // istft(normalized) multiplies by √n_fft, the inverse FFT here divides by n_fft.
        var scale = MathF.Sqrt(Size) / Size;
        var first = Size / 2 + Pad;
        for (var source = 0; source < sources; source++)
        {
            for (var channel = 0; channel < 2; channel++)
            {
                Array.Clear(signal);
                var reBase = ((source * 4) + channel * 2) * Bins * frames;
                var imBase = reBase + Bins * frames;
                for (var frame = 0; frame < frames; frame++)
                {
                    for (var bin = 0; bin < Bins; bin++)
                    {
                        re[bin] = spec[reBase + bin * frames + frame];
                        im[bin] = spec[imBase + bin * frames + frame];
                    }
                    // The dropped top bin is zero; the rest mirror (a real signal). The DC and top bins are real.
                    im[0] = 0;
                    re[Bins] = 0;
                    im[Bins] = 0;
                    for (var bin = 1; bin < Bins; bin++)
                    {
                        re[Size - bin] = re[bin];
                        im[Size - bin] = -im[bin];
                    }
                    Transform.Run(re, im, inverse: true);
                    var start = (frame + 2) * Hop;
                    for (var n = 0; n < Size; n++)
                    {
                        signal[start + n] += re[n] * scale * Window[n];
                    }
                }
                var target = output.Slice((source * 2 + channel) * length, length);
                for (var i = 0; i < length; i++)
                {
                    var e = envelope[first + i];
                    target[i] += e > 1e-11f ? signal[first + i] / e : 0;
                }
            }
        }
    }

    /// <summary>Reflection padding as PyTorch's: -1 → 1, length → length - 2.</summary>
    private static int Reflect(int index, int length)
    {
        if (length == 1)
        {
            return 0;
        }
        var period = 2 * (length - 1);
        index %= period;
        if (index < 0)
        {
            index += period;
        }
        return index < length ? index : period - index;
    }

    private static float[] MakeWindow()
    {
        var window = new float[Size];
        for (var n = 0; n < Size; n++)
        {
            window[n] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * n / Size));
        }
        return window;
    }

    /// <summary>An in-place radix-2 complex FFT of one power-of-two size.</summary>
    private sealed class Fft
    {
        private readonly int _size;
        private readonly int[] _reversed;
        private readonly float[] _cos;
        private readonly float[] _sin;

        public Fft(int size)
        {
            _size = size;
            _reversed = new int[size];
            var bits = (int)Math.Log2(size);
            for (var i = 0; i < size; i++)
            {
                var r = 0;
                for (var b = 0; b < bits; b++)
                {
                    r |= ((i >> b) & 1) << (bits - 1 - b);
                }
                _reversed[i] = r;
            }
            _cos = new float[size / 2];
            _sin = new float[size / 2];
            for (var i = 0; i < size / 2; i++)
            {
                _cos[i] = (float)Math.Cos(2 * Math.PI * i / size);
                _sin[i] = (float)Math.Sin(2 * Math.PI * i / size);
            }
        }

        public void Run(float[] re, float[] im, bool inverse)
        {
            for (var i = 0; i < _size; i++)
            {
                var j = _reversed[i];
                if (j > i)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }
            var sign = inverse ? 1 : -1;
            for (var length = 2; length <= _size; length <<= 1)
            {
                var half = length / 2;
                var step = _size / length;
                for (var start = 0; start < _size; start += length)
                {
                    for (var k = 0; k < half; k++)
                    {
                        var wr = _cos[k * step];
                        var wi = sign * _sin[k * step];
                        var a = start + k;
                        var b = a + half;
                        var tr = re[b] * wr - im[b] * wi;
                        var ti = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - tr;
                        im[b] = im[a] - ti;
                        re[a] += tr;
                        im[a] += ti;
                    }
                }
            }
        }
    }
}
