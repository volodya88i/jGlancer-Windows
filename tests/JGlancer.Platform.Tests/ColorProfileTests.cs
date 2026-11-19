using System.Buffers.Binary;
using System.Text;
using JGlancer.Core.Tests.Support;
using JGlancer.Media.Photos;
using JGlancer.Platform.Windows;

namespace JGlancer.Platform.Tests;

/// <summary>Wide-gamut photos are converted to sRGB on decode; sRGB and unprofiled ones pass unchanged (MAC-036).</summary>
public sealed class ColorProfileTests
{
    [Theory]
    [InlineData("sRGB IEC61966-2.1", true)]
    [InlineData("sRGB built-in", true)]
    [InlineData(null, true)]
    [InlineData("Display P3", false)]
    [InlineData("Adobe RGB (1998)", false)]
    public void OnlyProfilesOtherThanSrgbAreConverted(string? description, bool isSrgb) =>
        Assert.Equal(isSrgb, WicColor.IsSrgb(description));

    [Fact]
    public void APhotoWithADisplayP3ProfileIsShownInSrgb()
    {
        using var folder = new TemporaryDirectory();
        Fixtures.CopyTo(folder, "photo_landscape.jpg");
        var plain = folder.Full("photo_landscape.jpg");
        var wide = folder.Full("photo_p3.jpg");
        File.WriteAllBytes(wide, WithProfile(File.ReadAllBytes(plain), WideProfile("Display P3")));

        Assert.False(WicPhotoDecoder.HasWideProfile(plain));
        Assert.True(WicPhotoDecoder.HasWideProfile(wide));
        var decoder = new WicPhotoDecoder();
        var before = decoder.Decode(plain, 200, 200, default)!.Image;
        var after = decoder.Decode(wide, 200, 200, default)!.Image;
        Assert.Equal((before.Width, before.Height), (after.Width, after.Height));
        // The same numbers mean other colors under the profile: the decode is no longer the same.
        var changed = 0;
        for (var i = 0; i < before.Pixels.Length; i++)
        {
            if (Math.Abs(before.Pixels[i] - after.Pixels[i]) > 8)
            {
                changed++;
            }
        }
        Assert.True(changed > before.Pixels.Length / 20, $"{changed} of {before.Pixels.Length} bytes changed");
        // An sRGB profile changes nothing that matters.
        File.WriteAllBytes(wide, WithProfile(File.ReadAllBytes(plain), WideProfile("sRGB IEC61966-2.1")));
        Assert.False(WicPhotoDecoder.HasWideProfile(wide));
    }

    /// <summary>A JPEG with an APP2 ICC_PROFILE segment right after its start marker.</summary>
    private static byte[] WithProfile(byte[] jpeg, byte[] profile)
    {
        var marker = Encoding.ASCII.GetBytes("ICC_PROFILE\0");
        var length = 2 + marker.Length + 2 + profile.Length;
        using var output = new MemoryStream();
        output.Write(jpeg, 0, 2);
        output.Write([0xFF, 0xE2, (byte)(length >> 8), (byte)length]);
        output.Write(marker);
        output.Write([1, 1]);
        output.Write(profile);
        output.Write(jpeg, 2, jpeg.Length - 2);
        return output.ToArray();
    }

    /// <summary>
    /// A small ICC v2 display profile with Display P3's primaries (D50-adapted) and linear curves, under
    /// <paramref name="description"/>.
    /// </summary>
    private static byte[] WideProfile(string description)
    {
        var tags = new List<(string Signature, byte[] Data)>
        {
            ("desc", Description(description)),
            ("cprt", Text("No copyright, test data")),
            ("wtpt", Xyz(0.9642, 1.0, 0.8249)),
            ("rXYZ", Xyz(0.5151, 0.2412, -0.0011)),
            ("gXYZ", Xyz(0.2920, 0.6922, 0.0419)),
            ("bXYZ", Xyz(0.1571, 0.0666, 0.7841)),
            ("rTRC", Curve()),
            ("gTRC", Curve()),
            ("bTRC", Curve()),
        };
        var tableSize = 4 + tags.Count * 12;
        var offset = 128 + tableSize;
        var body = new MemoryStream();
        var table = new List<(string, int, int)>();
        foreach (var (signature, data) in tags)
        {
            table.Add((signature, offset + (int)body.Length, data.Length));
            body.Write(data);
            while (body.Length % 4 != 0)
            {
                body.WriteByte(0);
            }
        }
        var profile = new byte[(offset + body.Length + 3) / 4 * 4];
        var span = profile.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)profile.Length);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], 0x02100000);
        // 2026-01-01 00:00:00, a valid creation date.
        BinaryPrimitives.WriteUInt16BigEndian(span[24..], 2026);
        BinaryPrimitives.WriteUInt16BigEndian(span[26..], 1);
        BinaryPrimitives.WriteUInt16BigEndian(span[28..], 1);
        Encoding.ASCII.GetBytes("mntrRGB XYZ ").CopyTo(span[12..]);
        Encoding.ASCII.GetBytes("acsp").CopyTo(span[36..]);
        Xyz(0.9642, 1.0, 0.8249).AsSpan(8).CopyTo(span[68..]);
        BinaryPrimitives.WriteUInt32BigEndian(span[128..], (uint)tags.Count);
        for (var i = 0; i < table.Count; i++)
        {
            var entry = span[(132 + i * 12)..];
            Encoding.ASCII.GetBytes(table[i].Item1).CopyTo(entry);
            BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)table[i].Item2);
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)table[i].Item3);
        }
        body.ToArray().CopyTo(span[offset..]);
        return profile;
    }

    private static byte[] Xyz(double x, double y, double z)
    {
        var data = new byte[20];
        Encoding.ASCII.GetBytes("XYZ ").CopyTo(data, 0);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), (int)Math.Round(x * 65536));
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12), (int)Math.Round(y * 65536));
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), (int)Math.Round(z * 65536));
        return data;
    }

    /// <summary>A curve of gamma 1.0: linear.</summary>
    private static byte[] Curve()
    {
        var data = new byte[14];
        Encoding.ASCII.GetBytes("curv").CopyTo(data, 0);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12), 0x0100);
        return data;
    }

    private static byte[] Text(string text)
    {
        var ascii = Encoding.ASCII.GetBytes(text + "\0");
        var data = new byte[8 + ascii.Length];
        Encoding.ASCII.GetBytes("text").CopyTo(data, 0);
        ascii.CopyTo(data, 8);
        return data;
    }

    private static byte[] Description(string text)
    {
        var ascii = Encoding.ASCII.GetBytes(text + "\0");
        var data = new byte[12 + ascii.Length + 4 + 4 + 2 + 1 + 67];
        Encoding.ASCII.GetBytes("desc").CopyTo(data, 0);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), (uint)ascii.Length);
        ascii.CopyTo(data, 12);
        return data;
    }
}
