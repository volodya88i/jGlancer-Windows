namespace JGlancer.Core.Stems;

/// <summary>
/// The spectrogram Mel-Band RoFormer works on (MAC-056), computed here because the ONNX model holds only the network:
/// PyTorch's <c>stft</c> / <c>istft</c> with a 2048-point periodic Hann window, hop 441, not normalized, centred with
/// reflection, all 1025 bins. Layout: frame after frame, the bins of each frame together. One instance per thread (it
/// keeps its work buffers).
/// </summary>
public sealed class BandSpectrogram
{
    public const int Size = 2048;

    public const int Hop = 441;

    public const int Bins = Size / 2 + 1;

    private static readonly Fft Transform = new(Size);

    private static readonly float[] Window = MakeWindow();

    private readonly float[] _re = new float[Size];
    private readonly float[] _im = new float[Size];

    /// <summary>Frames of a piece of <paramref name="length"/> samples.</summary>
    public static int Frames(int length) => length / Hop + 1;

    /// <summary>
    /// The spectrum of one channel (more than 1024 samples) into <paramref name="real"/> / <paramref name="imaginary"/>
    /// (<see cref="Frames"/> × <see cref="Bins"/>).
    /// </summary>
    public void Forward(ReadOnlySpan<float> signal, Span<float> real, Span<float> imaginary)
    {
        var length = signal.Length;
        for (var frame = 0; frame < Frames(length); frame++)
        {
            // torch.stft centres frame t on sample t × hop, reflecting n_fft/2 samples past each end.
            var start = frame * Hop - Size / 2;
            for (var tap = 0; tap < Size; tap++)
            {
                _re[tap] = signal[Spectrogram.Reflect(start + tap, length)] * Window[tap];
                _im[tap] = 0;
            }
            Transform.Run(_re, _im, inverse: false);
            var target = frame * Bins;
            _re.AsSpan(0, Bins).CopyTo(real.Slice(target, Bins));
            _im.AsSpan(0, Bins).CopyTo(imaginary.Slice(target, Bins));
        }
    }

    /// <summary>
    /// The samples of one channel (<paramref name="output"/>'s length) from its spectrum, as torch.istft: the inverse
    /// of each frame windowed and overlapped, divided by the overlapping windows' squares.
    /// </summary>
    public void Inverse(ReadOnlySpan<float> real, ReadOnlySpan<float> imaginary, Span<float> output)
    {
        var length = output.Length;
        var frames = Frames(length);
        var total = Size + Hop * (frames - 1);
        var signal = new float[total];
        var envelope = new float[total];
        const int last = Bins - 1;
        for (var frame = 0; frame < frames; frame++)
        {
            // A full spectrum: the upper half mirrors the lower (a real signal); the first and last bins are real.
            var source = frame * Bins;
            real.Slice(source, Bins).CopyTo(_re);
            imaginary.Slice(source, Bins).CopyTo(_im);
            _im[0] = 0;
            _im[last] = 0;
            for (var bin = 1; bin < last; bin++)
            {
                _re[Size - bin] = _re[bin];
                _im[Size - bin] = -_im[bin];
            }
            Transform.Run(_re, _im, inverse: true);
            var start = frame * Hop;
            for (var n = 0; n < Size; n++)
            {
                // The inverse FFT here does not divide by n_fft.
                signal[start + n] += _re[n] / Size * Window[n];
                envelope[start + n] += Window[n] * Window[n];
            }
        }
        const int first = Size / 2;
        for (var i = 0; i < length; i++)
        {
            var weight = envelope[first + i];
            output[i] = weight > 1e-11f ? signal[first + i] / weight : 0;
        }
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
}
