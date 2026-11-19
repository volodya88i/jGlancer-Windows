using System.Buffers.Binary;
using System.Text;
using JGlancer.Core.Tests.Support;
using JGlancer.Platform.Windows;

namespace JGlancer.Platform.Tests;

/// <summary>Animated GIFs and PNGs: frames composed with their delays and loop counts (MAC-039).</summary>
public sealed class AnimatedImageTests
{
    [Fact]
    public void AGifPlaysItsFramesOverEachOther()
    {
        using var folder = new TemporaryDirectory();
        var path = folder.Full("two.gif");
        File.WriteAllBytes(path, TwoFrameGif());
        Assert.True(AnimatedImageReader.IsAnimated(path));
        using var reader = AnimatedImageReader.Open(path)!;
        Assert.Equal((2, 1, 2, 0), (reader.Width, reader.Height, reader.FrameCount, reader.LoopCount));
        var canvas = new byte[2 * 1 * 4];
        Assert.Equal(TimeSpan.FromMilliseconds(100), reader.Next(canvas));
        // Red, then nothing yet (BGRA).
        Assert.Equal(new byte[] { 0, 0, 255, 255, 0, 0, 0, 0 }, canvas);
        reader.Next(canvas);
        // The second frame lands beside the first, which stays (disposal 1).
        Assert.Equal(new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 }, canvas);
        Assert.Equal(0, reader.Index);
    }

    [Fact]
    public void AnApngPlaysAndAPlainPngIsStill()
    {
        using var folder = new TemporaryDirectory();
        Fixtures.CopyTo(folder, "photo_alpha.png");
        var still = folder.Full("photo_alpha.png");
        Assert.False(AnimatedImageReader.IsAnimated(still));
        Assert.Null(AnimatedImageReader.Open(still));
        Assert.False(AnimatedImageReader.IsAnimated(Fixtures.Folder + "\\photo.gif"));

        var animated = folder.Full("animated.png");
        File.WriteAllBytes(animated, TwoFramePng(File.ReadAllBytes(still)));
        Assert.True(AnimatedImageReader.IsAnimated(animated));
        using var reader = AnimatedImageReader.Open(animated)!;
        Assert.Equal((2, 3), (reader.FrameCount, reader.LoopCount));
        var canvas = new byte[reader.Width * reader.Height * 4];
        Assert.Equal(TimeSpan.FromMilliseconds(100), reader.Next(canvas));
        var expected = new WicPhotoDecoder().Decode(still, int.MaxValue, int.MaxValue, default)!.Image.Pixels;
        Assert.Equal(expected, canvas);
        reader.Next(canvas);
        Assert.Equal(expected, canvas);
    }

    /// <summary>A 2 × 1 GIF looping for ever: a red pixel at the left, then a blue one at the right, 100 ms each.</summary>
    private static byte[] TwoFrameGif()
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("GIF89a"));
        bytes.AddRange([2, 0, 1, 0, 0x80, 0, 0]);
        bytes.AddRange([0xFF, 0, 0, 0, 0, 0xFF]);
        bytes.AddRange([0x21, 0xFF, 0x0B]);
        bytes.AddRange(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        bytes.AddRange([3, 1, 0, 0, 0]);
        // Each frame: graphic control (do not dispose, 10/100 s), its place, one pixel of LZW (clear, index, end).
        bytes.AddRange([0x21, 0xF9, 4, 0x04, 10, 0, 0, 0, 0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x44, 0x01, 0]);
        bytes.AddRange([0x21, 0xF9, 4, 0x04, 10, 0, 0, 0, 0x2C, 1, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x4C, 0x01, 0]);
        bytes.Add(0x3B);
        return [.. bytes];
    }

    /// <summary>The PNG twice as an APNG playing 3 times: the default image as frame one, its data again as frame two.</summary>
    private static byte[] TwoFramePng(byte[] png)
    {
        byte[]? header = null;
        var before = new List<(string, byte[])>();
        var data = new List<byte[]>();
        for (var at = 8; at + 12 <= png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = Encoding.ASCII.GetString(png, at + 4, 4);
            var body = png.AsSpan(at + 8, length).ToArray();
            if (type == "IHDR")
            {
                header = body;
            }
            else if (type == "IDAT")
            {
                data.Add(body);
            }
            else if (type != "IEND" && data.Count == 0)
            {
                before.Add((type, body));
            }
            at += 12 + length;
        }
        using var output = new MemoryStream();
        output.Write(png, 0, 8);
        Chunk(output, "IHDR", header!);
        Chunk(output, "acTL", [.. Big(2), .. Big(3)]);
        foreach (var (type, body) in before)
        {
            Chunk(output, type, body);
        }
        var sequence = 0;
        Chunk(output, "fcTL", Control(sequence++, header!));
        foreach (var part in data)
        {
            Chunk(output, "IDAT", part);
        }
        Chunk(output, "fcTL", Control(sequence++, header!));
        foreach (var part in data)
        {
            Chunk(output, "fdAT", [.. Big(sequence++), .. part]);
        }
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>A whole-image frame control: 1/10 s, no disposal, drawn in place.</summary>
    private static byte[] Control(int sequence, byte[] header) =>
        [.. Big(sequence), .. header.AsSpan(0, 8), .. Big(0), .. Big(0), 0, 1, 0, 10, 0, 0];

    private static byte[] Big(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }

    private static void Chunk(Stream output, string type, byte[] body)
    {
        output.Write(Big(body.Length));
        var name = Encoding.ASCII.GetBytes(type);
        output.Write(name);
        output.Write(body);
        output.Write(Big((int)Crc([.. name, .. body])));
    }

    private static uint Crc(byte[] bytes)
    {
        var crc = 0xFFFFFFFF;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
        }
        return crc ^ 0xFFFFFFFF;
    }
}
