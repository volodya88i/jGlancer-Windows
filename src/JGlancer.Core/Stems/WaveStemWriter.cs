using System.Buffers.Binary;

namespace JGlancer.Core.Stems;

/// <summary>
/// A stem written as it is finished: a 24-bit stereo PCM WAVE file whose header gets its lengths when it is completed
/// (W-85). Samples outside -1…1 are clipped.
/// </summary>
public sealed class WaveStemWriter : IStemSink, IDisposable
{
    private const int HeaderSize = 44;
    private const int Channels = 2;
    private const int BytesPerSample = 3;

    private readonly Stream _stream;
    private readonly int _sampleRate;
    private long _dataBytes;
    private byte[] _buffer = [];

    public WaveStemWriter(Stream stream, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _sampleRate = sampleRate;
        _stream.Write(Header(0));
    }

    public void Write(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        var frames = Math.Min(left.Length, right.Length);
        var size = frames * Channels * BytesPerSample;
        if (_buffer.Length < size)
        {
            _buffer = new byte[size];
        }
        var at = 0;
        for (var i = 0; i < frames; i++)
        {
            Put(left[i], at);
            Put(right[i], at + BytesPerSample);
            at += Channels * BytesPerSample;
        }
        _stream.Write(_buffer, 0, size);
        _dataBytes += size;
    }

    /// <summary>Writes the final lengths into the header.</summary>
    public void Complete()
    {
        _stream.Seek(0, SeekOrigin.Begin);
        _stream.Write(Header(_dataBytes));
        _stream.Flush();
    }

    public void Dispose() => _stream.Dispose();

    private void Put(float sample, int at)
    {
        var value = (int)Math.Round(Math.Clamp(sample, -1f, 1f) * 8_388_607f);
        _buffer[at] = (byte)value;
        _buffer[at + 1] = (byte)(value >> 8);
        _buffer[at + 2] = (byte)(value >> 16);
    }

    private byte[] Header(long dataBytes)
    {
        var header = new byte[HeaderSize];
        var span = header.AsSpan();
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)Math.Min(uint.MaxValue, dataBytes + HeaderSize - 8));
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[22..], Channels);
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint)_sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..], (uint)(_sampleRate * Channels * BytesPerSample));
        BinaryPrimitives.WriteUInt16LittleEndian(span[32..], Channels * BytesPerSample);
        BinaryPrimitives.WriteUInt16LittleEndian(span[34..], BytesPerSample * 8);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..], (uint)Math.Min(uint.MaxValue, dataBytes));
        return header;
    }
}
