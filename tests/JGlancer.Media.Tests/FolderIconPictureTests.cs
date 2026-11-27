using JGlancer.Core.Model;
using JGlancer.Media.Icons;
using JGlancer.Media.Thumbnails;

namespace JGlancer.Media.Tests;

/// <summary>Folder icon pictures (macOS D-129): the middle square of the photo at 96 pixels, as JPEG.</summary>
public sealed class FolderIconPictureTests
{
    [Fact]
    public void TheMiddleSquareBecomesA96PixelJpeg()
    {
        // 300 × 100: red, green in the middle third, blue.
        var pixels = new byte[300 * 100 * 4];
        for (var y = 0; y < 100; y++)
        {
            for (var x = 0; x < 300; x++)
            {
                var i = (y * 300 + x) * 4;
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = x < 100 ? ((byte)0, (byte)0, (byte)255, (byte)255)
                    : x < 200 ? ((byte)0, (byte)255, (byte)0, (byte)255) : ((byte)255, (byte)0, (byte)0, (byte)255);
            }
        }
        var jpeg = FolderIconPicture.Make(new ThumbnailImage(300, 100, pixels));
        Assert.NotNull(jpeg);
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);
        var decoded = ThumbnailCodec.Decode(jpeg)!;
        Assert.Equal((FolderIcon.PictureSide, FolderIcon.PictureSide), (decoded.Width, decoded.Height));
        // BGRA: the middle stays green.
        var centre = (48 * 96 + 48) * 4;
        Assert.True(decoded.Pixels[centre + 1] > 200 && decoded.Pixels[centre + 2] < 60 && decoded.Pixels[centre] < 60);
    }
}
