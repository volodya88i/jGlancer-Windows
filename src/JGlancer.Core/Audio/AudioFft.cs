namespace JGlancer.Core.Audio;

/// <summary>Magnitudes of a Hann-windowed block, by a radix-2 FFT; one instance per thread.</summary>
internal sealed class AudioFft
{
    private readonly int _size;
    private readonly float[] _window;
    private readonly float[] _re;
    private readonly float[] _im;

    public AudioFft(int size)
    {
        _size = size;
        _window = new float[size];
        for (var i = 0; i < size; i++)
        {
            _window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size));
        }
        _re = new float[size];
        _im = new float[size];
    }

    /// <summary>Writes the magnitudes of bins 0 … size/2 of the block at <paramref name="start"/>, scaled so full scale is about 1.</summary>
    public void Magnitudes(ReadOnlySpan<short> samples, int start, Span<float> output)
    {
        for (var i = 0; i < _size; i++)
        {
            _re[i] = samples[start + i] / 32768f * _window[i];
            _im[i] = 0;
        }
        Transform();
        var scale = 4f / _size;
        for (var i = 0; i <= _size / 2; i++)
        {
            output[i] = MathF.Sqrt(_re[i] * _re[i] + _im[i] * _im[i]) * scale;
        }
    }

    private void Transform()
    {
        for (int i = 1, j = 0; i < _size; i++)
        {
            var bit = _size >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }
            j ^= bit;
            if (i < j)
            {
                (_re[i], _re[j]) = (_re[j], _re[i]);
                (_im[i], _im[j]) = (_im[j], _im[i]);
            }
        }
        for (var length = 2; length <= _size; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            for (var start = 0; start < _size; start += length)
            {
                for (var k = 0; k < length / 2; k++)
                {
                    var wr = (float)Math.Cos(angle * k);
                    var wi = (float)Math.Sin(angle * k);
                    var a = start + k;
                    var b = a + length / 2;
                    var tr = _re[b] * wr - _im[b] * wi;
                    var ti = _re[b] * wi + _im[b] * wr;
                    _re[b] = _re[a] - tr;
                    _im[b] = _im[a] - ti;
                    _re[a] += tr;
                    _im[a] += ti;
                }
            }
        }
    }
}
