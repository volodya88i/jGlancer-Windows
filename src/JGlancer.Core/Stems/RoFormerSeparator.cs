namespace JGlancer.Core.Stems;

/// <summary>
/// The network inside Mel-Band RoFormer: from each mel band's bins (<see cref="MelBands.Features"/> per frame) to their
/// complex vocals masks, same layout. The ONNX model in the app, a stand-in in the tests.
/// </summary>
public interface IBandMaskNetwork
{
    /// <param name="bands">Frames × <see cref="MelBands.Features"/> values.</param>
    /// <param name="masks">The masks, as many values.</param>
    void Run(float[] bands, Span<float> masks);
}

/// <summary>
/// Mel-Band RoFormer (MAC-056) as an <see cref="IStemSeparator"/> with one source, the vocals: the C# spectrogram, each
/// mel band's bins gathered for the network, its complex masks averaged where bands overlap and applied, then the C#
/// inverse. Pieces of 8 s overlapping by half, the audio as it is (no normalizing), as the model's own inference.
/// </summary>
public sealed class RoFormerSeparator : IStemSeparator
{
    /// <summary>The piece the network takes: 8 s at 44.1 kHz, 801 frames.</summary>
    public const int Segment = 352_800;

    private readonly IBandMaskNetwork _network;
    private readonly BandSpectrogram _spectrogram = new();
    private readonly int _frames = BandSpectrogram.Frames(Segment);
    private readonly float[][] _real;
    private readonly float[][] _imaginary;
    private readonly float[][] _maskReal;
    private readonly float[][] _maskImaginary;
    private readonly float[] _bands;
    private readonly float[] _masks;

    public RoFormerSeparator(IBandMaskNetwork network)
    {
        ArgumentNullException.ThrowIfNull(network);
        _network = network;
        var size = _frames * BandSpectrogram.Bins;
        _real = [new float[size], new float[size]];
        _imaginary = [new float[size], new float[size]];
        _maskReal = [new float[size], new float[size]];
        _maskImaginary = [new float[size], new float[size]];
        _bands = new float[_frames * MelBands.Features];
        _masks = new float[_bands.Length];
    }

    public int Sources => 1;

    public int SegmentFrames => Segment;

    /// <summary>Half, as the model's own inference.</summary>
    public double Overlap => 0.5;

    /// <summary>The audio as it is.</summary>
    public bool Normalizes => false;

    public void Separate(ReadOnlySpan<float> input, Span<float> output)
    {
        for (var channel = 0; channel < 2; channel++)
        {
            _spectrogram.Forward(input.Slice(channel * Segment, Segment), _real[channel], _imaginary[channel]);
        }
        Gather();
        _network.Run(_bands, _masks);
        Average();
        for (var channel = 0; channel < 2; channel++)
        {
            Apply(channel);
            _spectrogram.Inverse(_real[channel], _imaginary[channel], output.Slice(channel * Segment, Segment));
        }
    }

    /// <summary>The network's input: per frame, each band's bins, left then right, real then imaginary.</summary>
    private void Gather()
    {
        var index = 0;
        for (var frame = 0; frame < _frames; frame++)
        {
            var row = frame * BandSpectrogram.Bins;
            foreach (var bin in MelBands.Bins)
            {
                for (var channel = 0; channel < 2; channel++)
                {
                    _bands[index++] = _real[channel][row + bin];
                    _bands[index++] = _imaginary[channel][row + bin];
                }
            }
        }
    }

    /// <summary>The masks of all bands added up per bin and divided by the number of bands the bin is in.</summary>
    private void Average()
    {
        for (var channel = 0; channel < 2; channel++)
        {
            Array.Clear(_maskReal[channel]);
            Array.Clear(_maskImaginary[channel]);
        }
        var index = 0;
        for (var frame = 0; frame < _frames; frame++)
        {
            var row = frame * BandSpectrogram.Bins;
            foreach (var bin in MelBands.Bins)
            {
                for (var channel = 0; channel < 2; channel++)
                {
                    _maskReal[channel][row + bin] += _masks[index++];
                    _maskImaginary[channel][row + bin] += _masks[index++];
                }
            }
        }
        for (var channel = 0; channel < 2; channel++)
        {
            for (var frame = 0; frame < _frames; frame++)
            {
                var row = frame * BandSpectrogram.Bins;
                for (var bin = 0; bin < BandSpectrogram.Bins; bin++)
                {
                    _maskReal[channel][row + bin] /= MelBands.Coverage[bin];
                    _maskImaginary[channel][row + bin] /= MelBands.Coverage[bin];
                }
            }
        }
    }

    /// <summary>The spectrum times its mask, as complex numbers.</summary>
    private void Apply(int channel)
    {
        var (real, imaginary) = (_real[channel], _imaginary[channel]);
        var (maskReal, maskImaginary) = (_maskReal[channel], _maskImaginary[channel]);
        for (var i = 0; i < real.Length; i++)
        {
            var (re, im) = (real[i], imaginary[i]);
            real[i] = re * maskReal[i] - im * maskImaginary[i];
            imaginary[i] = re * maskImaginary[i] + im * maskReal[i];
        }
    }
}
