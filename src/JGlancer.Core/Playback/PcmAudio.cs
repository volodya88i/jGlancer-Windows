using System.Buffers.Binary;
using System.Text;

namespace JGlancer.Core.Playback;

/// <summary>Interleaved PCM: little-endian integers of <see cref="BitsPerSample"/> bits, or 32-bit floats.</summary>
public sealed record PcmAudio(int SampleRate, int Channels, int BitsPerSample, bool IsFloat, byte[] Data)
{
    public int BlockAlign => Channels * BitsPerSample / 8;

    public long Frames => BlockAlign == 0 ? 0 : Data.Length / BlockAlign;

    public double Seconds => SampleRate == 0 ? 0 : (double)Frames / SampleRate;

    /// <summary>The first <paramref name="frames"/> frames (an encoder padded the end with silence).</summary>
    public PcmAudio Truncated(long frames) =>
        frames >= Frames ? this : this with { Data = Data[..(int)(Math.Max(0, frames) * BlockAlign)] };
}

/// <summary>WAVE and AIFF files of PCM (segment export, W-40).</summary>
public static class PcmFiles
{
    private const ushort FormatPcm = 1;
    private const ushort FormatFloat = 3;
    private const ushort FormatExtensible = 0xFFFE;

    /// <summary>
    /// The PCM of a RIFF/WAVE file (PCM, float or extensible). A "data" length of 0 or past the end (a header written
    /// before the end was known) means "to the end of the file". Null when it is not such a file.
    /// </summary>
    public static PcmAudio? ReadWave(ReadOnlySpan<byte> file)
    {
        if (file.Length < 12 || !file[..4].SequenceEqual("RIFF"u8) || !file[8..12].SequenceEqual("WAVE"u8))
        {
            return null;
        }
        int? rate = null, channels = null, bits = null;
        var isFloat = false;
        var position = 12;
        while (position + 8 <= file.Length)
        {
            var id = file.Slice(position, 4);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(position + 4, 4));
            var start = position + 8;
            if (id.SequenceEqual("fmt "u8) && start + 16 <= file.Length)
            {
                var tag = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(start, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(start + 2, 2));
                rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(start + 4, 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(start + 14, 2));
                if (tag == FormatExtensible && length >= 40 && start + 26 <= file.Length)
                {
                    tag = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(start + 24, 2));
                }
                isFloat = tag == FormatFloat;
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (rate is not { } r || channels is not { } c || bits is not { } b || c == 0 || b == 0)
                {
                    return null;
                }
                var available = file.Length - start;
                var bytes = length == 0 || length > available ? available : (int)length;
                var block = c * b / 8;
                bytes -= bytes % block;
                return new PcmAudio(r, c, b, isFloat, file.Slice(start, bytes).ToArray());
            }
            position = start + (int)Math.Min(length, int.MaxValue - start) + (int)(length % 2);
        }
        return null;
    }

    /// <summary>A canonical WAVE file (PCM, or IEEE float for float samples).</summary>
    public static byte[] WriteWave(PcmAudio audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        using var stream = new MemoryStream(44 + audio.Data.Length);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + audio.Data.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write(audio.IsFloat ? FormatFloat : FormatPcm);
        writer.Write((ushort)audio.Channels);
        writer.Write(audio.SampleRate);
        writer.Write(audio.SampleRate * audio.BlockAlign);
        writer.Write((ushort)audio.BlockAlign);
        writer.Write((ushort)audio.BitsPerSample);
        writer.Write("data"u8);
        writer.Write(audio.Data.Length);
        writer.Write(audio.Data);
        if (audio.Data.Length % 2 == 1)
        {
            writer.Write((byte)0);
        }
        return stream.ToArray();
    }

    /// <summary>An AIFF file: big-endian integer PCM (float samples are written as 32-bit integers).</summary>
    public static byte[] WriteAiff(PcmAudio audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        var bits = audio.IsFloat ? 32 : audio.BitsPerSample;
        var bytesPerSample = bits / 8;
        var data = new byte[audio.Frames * audio.Channels * bytesPerSample];
        var samples = audio.Frames * audio.Channels;
        for (long i = 0; i < samples; i++)
        {
            var source = audio.Data.AsSpan((int)(i * (audio.BitsPerSample / 8)), audio.BitsPerSample / 8);
            var target = data.AsSpan((int)(i * bytesPerSample), bytesPerSample);
            if (audio.IsFloat)
            {
                var value = Math.Clamp(BinaryPrimitives.ReadSingleLittleEndian(source), -1f, 1f);
                BinaryPrimitives.WriteInt32BigEndian(target, (int)Math.Clamp(value * 2147483648.0, int.MinValue, int.MaxValue));
            }
            else
            {
                for (var b = 0; b < bytesPerSample; b++)
                {
                    target[b] = source[bytesPerSample - 1 - b];
                }
            }
        }
        using var stream = new MemoryStream(54 + data.Length);
        using var writer = new BinaryWriter(stream);
        void BigEndian32(int value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            writer.Write(buffer);
        }
        void BigEndian16(short value)
        {
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteInt16BigEndian(buffer, value);
            writer.Write(buffer);
        }
        writer.Write("FORM"u8);
        BigEndian32(4 + 26 + 16 + data.Length);
        writer.Write("AIFF"u8);
        writer.Write("COMM"u8);
        BigEndian32(18);
        BigEndian16((short)audio.Channels);
        BigEndian32((int)audio.Frames);
        BigEndian16((short)bits);
        writer.Write(ExtendedFloat(audio.SampleRate));
        writer.Write("SSND"u8);
        BigEndian32(8 + data.Length);
        BigEndian32(0);
        BigEndian32(0);
        writer.Write(data);
        return stream.ToArray();
    }

    /// <summary>The 80-bit IEEE extended float AIFF uses for its sample rate.</summary>
    internal static byte[] ExtendedFloat(int value)
    {
        var bytes = new byte[10];
        if (value <= 0)
        {
            return bytes;
        }
        var exponent = 16383 + 63;
        var mantissa = (ulong)value;
        while ((mantissa & 0x8000000000000000) == 0)
        {
            mantissa <<= 1;
            exponent--;
        }
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)exponent);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(2), mantissa);
        return bytes;
    }

    /// <summary>ASCII of a four-character code (diagnostics).</summary>
    public static string FourCC(ReadOnlySpan<byte> code) => Encoding.ASCII.GetString(code);
}
