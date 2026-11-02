using System.Buffers.Binary;
using JGlancer.Core.Playback;

namespace JGlancer.Core.Tests.Playback;

public sealed class PcmFilesTests
{
    private static PcmAudio Stereo16(params short[] samples)
    {
        var data = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2), samples[i]);
        }
        return new PcmAudio(44100, 2, 16, false, data);
    }

    [Fact]
    public void WaveFilesRoundTripAndTruncateToWholeFrames()
    {
        var audio = Stereo16(1, -1, 2, -2, 3, -3);
        Assert.Equal(3, audio.Frames);
        var read = PcmFiles.ReadWave(PcmFiles.WriteWave(audio))!;
        Assert.Equal((44100, 2, 16, false), (read.SampleRate, read.Channels, read.BitsPerSample, read.IsFloat));
        Assert.Equal(audio.Data, read.Data);
        Assert.Equal(Stereo16(1, -1, 2, -2).Data, audio.Truncated(2).Data);
        Assert.Same(audio, audio.Truncated(10));
        Assert.Null(PcmFiles.ReadWave("not a wave"u8));
    }

    [Fact]
    public void AnUnknownDataLengthMeansToTheEnd()
    {
        var bytes = PcmFiles.WriteWave(Stereo16(5, 6, 7, 8));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 0xFFFFFFFF);
        Assert.Equal(2, PcmFiles.ReadWave(bytes)!.Frames);
    }

    [Fact]
    public void AiffIsBigEndianWithAnExtendedFloatRate()
    {
        var aiff = PcmFiles.WriteAiff(Stereo16(0x0102, 0x0304));
        Assert.Equal("FORM", PcmFiles.FourCC(aiff.AsSpan(0, 4)));
        Assert.Equal("AIFF", PcmFiles.FourCC(aiff.AsSpan(8, 4)));
        // 44100 Hz as an 80-bit extended float.
        Assert.Equal(new byte[] { 0x40, 0x0E, 0xAC, 0x44, 0, 0, 0, 0, 0, 0 }, PcmFiles.ExtendedFloat(44100));
        // The samples come last, big-endian.
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, aiff[^4..]);
    }
}
