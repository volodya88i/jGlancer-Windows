using JGlancer.Core.Model;
using JGlancer.Core.Tests.Support;
using JGlancer.Platform.Windows;

namespace JGlancer.Platform.Tests;

/// <summary>The macOS MediaDetailsTests on the Windows reader (fixtures copied to a temp folder first).</summary>
public sealed class MediaDetailsReaderTests
{
    private static MediaDetails Read(TemporaryDirectory folder, string name, MediaKind kind)
    {
        Fixtures.CopyTo(folder, name);
        return MediaDetailsReader.Read(folder.Full(name), kind);
    }

    [Fact]
    public void PhotosGiveCameraAndBitDepth()
    {
        using var folder = new TemporaryDirectory();
        var photo = Read(folder, "photo_orientation6.jpg", MediaKind.Photo);
        Assert.Equal("jGlancer Fixture", photo.Camera);
        // 24 bits per pixel → 8 per channel (macOS kCGImagePropertyDepth).
        Assert.Equal(8, photo.BitDepth);
        Assert.Null(photo.VideoCodec);
    }

    [Fact]
    public void ExposureLensAndLocationComeFromExif()
    {
        using var folder = new TemporaryDirectory();
        Fixtures.CopyTo(folder, "photo_landscape.jpg");
        var path = folder.Full("photo_landscape.jpg");
        File.WriteAllBytes(path, WithExif(File.ReadAllBytes(path)));
        var photo = MediaDetailsReader.Read(path, MediaKind.Photo);
        Assert.Equal("Apple iPhone 15 Pro", photo.Camera);
        Assert.Equal("iPhone 15 Pro back camera", photo.Lens);
        Assert.Equal(1.8, photo.Aperture!.Value, 3);
        Assert.Equal(0.008, photo.ExposureTime!.Value, 5);
        Assert.Equal(6.86, photo.FocalLength!.Value, 2);
        Assert.Equal(125, photo.Iso);
        Assert.Equal(55.75, photo.Latitude!.Value, 4);
        Assert.Equal(-37.62, photo.Longitude!.Value, 4);
    }

    /// <summary>
    /// The JPEG with its APP1 replaced by a little-endian EXIF block: make, model, lens, f/1.8, 1/125 s, 6.86 mm,
    /// ISO 125, GPS 55° 45' N, 37° 37' 12" W.
    /// </summary>
    private static byte[] WithExif(byte[] jpeg)
    {
        var tiff = new TiffWriter();
        var ifd0 = tiff.Ifd();
        ifd0.Ascii(0x010F, "Apple");
        ifd0.Ascii(0x0110, "iPhone 15 Pro");
        var exif = tiff.Ifd();
        exif.Rational(0x829D, [(18, 10)]);
        exif.Rational(0x829A, [(1, 125)]);
        exif.Short(0x8827, 125);
        exif.Rational(0x920A, [(686, 100)]);
        exif.Ascii(0xA434, "iPhone 15 Pro back camera");
        var gps = tiff.Ifd();
        gps.Ascii(0x0001, "N");
        gps.Rational(0x0002, [(55, 1), (45, 1), (0, 1)]);
        gps.Ascii(0x0003, "W");
        gps.Rational(0x0004, [(37, 1), (37, 1), (12, 1)]);
        ifd0.Pointer(0x8769, exif);
        ifd0.Pointer(0x8825, gps);
        var block = tiff.Bytes(ifd0);
        var app1 = new List<byte> { 0xFF, 0xE1 };
        var length = block.Length + 8;
        app1.Add((byte)(length >> 8));
        app1.Add((byte)length);
        app1.AddRange("Exif\0\0"u8.ToArray());
        app1.AddRange(block);
        // Keep SOI, drop the old APP1, keep everything else.
        var result = new List<byte> { 0xFF, 0xD8 };
        result.AddRange(app1);
        var position = 2;
        while (position + 4 <= jpeg.Length && jpeg[position] == 0xFF && jpeg[position + 1] != 0xDA)
        {
            var size = (jpeg[position + 2] << 8) | jpeg[position + 3];
            if (jpeg[position + 1] != 0xE1)
            {
                result.AddRange(jpeg.AsSpan(position, size + 2).ToArray());
            }
            position += size + 2;
        }
        result.AddRange(jpeg.AsSpan(position).ToArray());
        return [.. result];
    }

    /// <summary>A tiny little-endian TIFF writer: IFDs laid out one after another, values after each IFD.</summary>
    private sealed class TiffWriter
    {
        private readonly List<Ifd> _ifds = [];

        public Ifd Ifd()
        {
            var ifd = new Ifd();
            _ifds.Add(ifd);
            return ifd;
        }

        public byte[] Bytes(Ifd first)
        {
            // Offsets: header (8), then each IFD with its out-of-line values.
            var offset = 8;
            foreach (var ifd in _ifds)
            {
                ifd.Offset = offset;
                offset += ifd.Size;
            }
            var bytes = new List<byte> { (byte)'I', (byte)'I', 42, 0 };
            Add(bytes, (uint)first.Offset);
            foreach (var ifd in _ifds)
            {
                ifd.Write(bytes);
            }
            return [.. bytes];
        }

        internal static void Add(List<byte> bytes, uint value) => bytes.AddRange(BitConverter.GetBytes(value));

        internal static void Add(List<byte> bytes, ushort value) => bytes.AddRange(BitConverter.GetBytes(value));
    }

    private sealed class Ifd
    {
        private readonly List<(ushort Tag, ushort Type, uint Count, byte[] Value, Ifd? Target)> _entries = [];

        public int Offset { get; set; }

        public int Size => 2 + _entries.Count * 12 + 4 + _entries.Sum(e => e.Value.Length > 4 ? e.Value.Length : 0);

        public void Ascii(ushort tag, string text)
        {
            var value = System.Text.Encoding.ASCII.GetBytes(text + "\0");
            _entries.Add((tag, 2, (uint)value.Length, value, null));
        }

        public void Short(ushort tag, ushort value) => _entries.Add((tag, 3, 1, BitConverter.GetBytes(value), null));

        public void Rational(ushort tag, (uint Numerator, uint Denominator)[] values) =>
            _entries.Add((tag, 5, (uint)values.Length, [.. values.SelectMany(v => BitConverter.GetBytes(v.Numerator).Concat(BitConverter.GetBytes(v.Denominator)))], null));

        public void Pointer(ushort tag, Ifd target) => _entries.Add((tag, 4, 1, new byte[4], target));

        public void Write(List<byte> bytes)
        {
            var entries = _entries.OrderBy(e => e.Tag).ToList();
            TiffWriter.Add(bytes, (ushort)entries.Count);
            var data = Offset + 2 + entries.Count * 12 + 4;
            var outOfLine = new List<byte>();
            foreach (var (tag, type, count, value, target) in entries)
            {
                TiffWriter.Add(bytes, tag);
                TiffWriter.Add(bytes, type);
                TiffWriter.Add(bytes, count);
                if (target is not null)
                {
                    TiffWriter.Add(bytes, (uint)target.Offset);
                }
                else if (value.Length <= 4)
                {
                    bytes.AddRange(value);
                    bytes.AddRange(new byte[4 - value.Length]);
                }
                else
                {
                    TiffWriter.Add(bytes, (uint)(data + outOfLine.Count));
                    outOfLine.AddRange(value);
                }
            }
            TiffWriter.Add(bytes, 0u);
            bytes.AddRange(outOfLine);
        }
    }

    [Fact]
    public void VideoGivesCodecsRatesAndChannels()
    {
        using var folder = new TemporaryDirectory();
        var video = Read(folder, "clip_with_audio.mp4", MediaKind.Video);
        Assert.Equal("H.264", video.VideoCodec);
        Assert.Equal(30, video.FrameRate!.Value, 1);
        Assert.True(video.VideoBitrate > 0);
        Assert.Equal("AAC", video.AudioCodec);
        Assert.Equal(44_100, video.SampleRate);
        Assert.Equal(2, video.Channels);
        Assert.Null(video.BitDepth);
    }

    [Fact]
    public void SongTagsAreReadBeforePlaying()
    {
        using var folder = new TemporaryDirectory();
        Fixtures.CopyTo(folder, "tone_with_cover.m4a", "tone_440_mono.wav");
        Assert.Equal(new AudioTags.Tags("Fixture Tone", "jGlancer", "Test Media"), AudioTags.Read(folder.Full("tone_with_cover.m4a")));
        Assert.True(AudioTags.Read(folder.Full("tone_440_mono.wav")) is { Title: null });
    }

    [Fact]
    public void WaveIsPcmWithItsDepth()
    {
        using var folder = new TemporaryDirectory();
        var wave = Read(folder, "tone_440_mono.wav", MediaKind.Audio);
        Assert.Equal("PCM", wave.AudioCodec);
        Assert.Equal(1, wave.Channels);
        Assert.True(wave.SampleRate > 0);
        Assert.True(wave.BitDepth > 0);
    }

    [Fact]
    public async Task FilesWithoutAHandlerAreAskedOfMpv()
    {
        using var folder = new TemporaryDirectory();
        Assert.True(Read(folder, "tone_stereo.aiff", MediaKind.Audio) is { AudioCodec: null });
        if (!JGlancer.Mpv.MpvPlayer.IsAvailable)
        {
            return;
        }
        var aiff = await MediaDetailsReader.ReadAsync(folder.Full("tone_stereo.aiff"), MediaKind.Audio, TestContext.Current.CancellationToken);
        Assert.Equal("PCM", aiff.AudioCodec);
        Assert.Equal(2, aiff.Channels);
        Assert.True(aiff.SampleRate > 0);
        Assert.True(aiff.BitDepth > 0);
    }

    [Fact]
    public void IccProfilesNameThemselves()
    {
        // Windows' own sRGB profile (version 2, a 'desc' tag), read only.
        var system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "spool", "drivers", "color", "sRGB Color Space Profile.icm");
        if (File.Exists(system))
        {
            Assert.Contains("sRGB", IccProfile.Description(File.ReadAllBytes(system)), StringComparison.Ordinal);
        }
        // A version-4 'mluc' description.
        var text = System.Text.Encoding.BigEndianUnicode.GetBytes("Display P3");
        var tag = new byte[28 + text.Length];
        "mluc"u8.CopyTo(tag);
        WriteUInt(tag, 8, 1);
        WriteUInt(tag, 12, 12);
        "enUS"u8.CopyTo(tag.AsSpan(16));
        WriteUInt(tag, 20, (uint)text.Length);
        WriteUInt(tag, 24, 28);
        text.CopyTo(tag, 28);
        var profile = new byte[144 + tag.Length];
        WriteUInt(profile, 128, 1);
        "desc"u8.CopyTo(profile.AsSpan(132));
        WriteUInt(profile, 136, 144);
        WriteUInt(profile, 140, (uint)tag.Length);
        tag.CopyTo(profile, 144);
        Assert.Equal("Display P3", IccProfile.Description(profile));
        Assert.Null(IccProfile.Description(new byte[10]));
    }

    private static void WriteUInt(byte[] bytes, int offset, uint value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
}
