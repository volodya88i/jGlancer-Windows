using JGlancer.Core.Tests.Support;
using JGlancer.Media.Photos;
using JGlancer.Platform.Windows;

namespace JGlancer.Platform.Tests;

public sealed class PhotoDecoderTests
{
    [Fact]
    public void ScreenSizeDecodesKnowTheFullOrientedSize()
    {
        using var folder = new TemporaryDirectory();
        Fixtures.CopyTo(folder, "photo_landscape.jpg");
        Fixtures.CopyTo(folder, "photo_orientation6.jpg");
        var decoder = new WicPhotoDecoder();

        var full = decoder.Decode(folder.Full("photo_landscape.jpg"), IPhotoDecoder.Unbounded, IPhotoDecoder.Unbounded, default)!;
        Assert.True(full.IsFullResolution);
        Assert.Equal((full.FullWidth, full.FullHeight), (full.Image.Width, full.Image.Height));

        var small = decoder.Decode(folder.Full("photo_landscape.jpg"), 100, 100, default)!;
        Assert.False(small.IsFullResolution);
        Assert.Equal(100, small.Image.Width);
        Assert.Equal((full.FullWidth, full.FullHeight), (small.FullWidth, small.FullHeight));

        // Orientation 6 stands the landscape pixels up: the full size reports the shown (portrait) shape.
        var portrait = decoder.Decode(folder.Full("photo_orientation6.jpg"), 100, 100, default)!;
        Assert.True(portrait.FullHeight > portrait.FullWidth);
        Assert.True(portrait.Image.Height > portrait.Image.Width);
    }

    [Theory]
    [InlineData("photo_landscape.jpg")]
    [InlineData("photo_orientation6.jpg")]
    public void DecodingIntoGivenMemoryMatchesTheDecode(string name)
    {
        using var folder = new TemporaryDirectory();
        Fixtures.CopyTo(folder, name);
        var decoder = new WicPhotoDecoder();
        var expected = decoder.Decode(folder.Full(name), 120, 120, default)!;

        // A padded stride, as a bitmap's rows may have.
        byte[]? pixels = null;
        var stride = 0;
        System.Runtime.InteropServices.GCHandle handle = default;
        try
        {
            var full = decoder.DecodeInto(folder.Full(name), 120, 120, (width, height) =>
            {
                stride = width * 4 + 64;
                pixels = new byte[stride * height];
                handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
                return new PixelTarget(handle.AddrOfPinnedObject(), stride);
            }, default);

            Assert.Equal((expected.FullWidth, expected.FullHeight), full);
            var image = expected.Image;
            for (var y = 0; y < image.Height; y++)
            {
                Assert.True(pixels.AsSpan(y * stride, image.Stride).SequenceEqual(image.Pixels.AsSpan(y * image.Stride, image.Stride)), $"row {y}");
            }
        }
        finally
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }
        Assert.Null(decoder.DecodeInto(folder.Full("missing.jpg"), 120, 120, (_, _) => null, default));
    }

    [Fact]
    public void UnreadableFilesAndCancelledRequestsGiveNothing()
    {
        using var folder = new TemporaryDirectory();
        Fixtures.CopyTo(folder, "corrupt_garbage.jpg");
        Fixtures.CopyTo(folder, "photo_landscape.jpg");
        var decoder = new WicPhotoDecoder();
        Assert.Null(decoder.Decode(folder.Full("corrupt_garbage.jpg"), 640, 640, default));
        Assert.Null(decoder.Decode(folder.Full("missing.jpg"), 640, 640, default));
        Assert.Null(decoder.Decode(folder.Full("photo_landscape.jpg"), 640, 640, new CancellationToken(canceled: true)));
    }
}
