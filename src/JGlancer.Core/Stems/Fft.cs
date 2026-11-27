namespace JGlancer.Core.Stems;

/// <summary>An in-place radix-2 complex FFT of one power-of-two size.</summary>
internal sealed class Fft
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
