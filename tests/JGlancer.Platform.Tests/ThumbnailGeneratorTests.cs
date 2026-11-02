using JGlancer.Core.Model;
using JGlancer.Core.Tests.Support;
using JGlancer.Media.Thumbnails;
using JGlancer.Platform.Windows;

namespace JGlancer.Platform.Tests;

public sealed class ThumbnailGeneratorTests
{
    private static ThumbnailImage? Generate(TemporaryDirectory folder, string name, MediaKind kind, int maxPixelSize = 320)
    {
        Fixtures.CopyTo(folder, name);
        var request = new ThumbnailRequest(1, folder.Full(name), kind, MediaClassifierType(name), DateTimeOffset.UnixEpoch, null, ThumbnailBucket.Medium);
        return new WindowsThumbnailGenerator().Generate(request, maxPixelSize, default);
    }

    private static string? MediaClassifierType(string name) => Core.Scanning.MediaClassifier.Classify(name)?.TypeIdentifier;

    /// <summary>Whether the pixel at (x, y) is the fixtures' red marker (BGRA).</summary>
    private static bool IsRed(ThumbnailImage image, int x, int y)
    {
        var i = (y * image.Width + x) * 4;
        return image.Pixels[i + 2] > 200 && image.Pixels[i + 1] < 60 && image.Pixels[i] < 60;
    }

    [Fact]
    public void PhotosAreDownsampledWithOrientationAndNeverUpscaled()
    {
        using var folder = new TemporaryDirectory();
        var landscape = Generate(folder, "photo_landscape.jpg", MediaKind.Photo)!;
        Assert.Equal((320, 240), (landscape.Width, landscape.Height));
        // The marker sits at the top-left of the stored pixels…
        Assert.True(IsRed(landscape, 2, 2));
        var portrait = Generate(folder, "photo_orientation6.jpg", MediaKind.Photo)!;
        Assert.Equal((240, 320), (portrait.Width, portrait.Height));
        // …and orientation 6 (rotate 90° clockwise) shows it at the top-right.
        Assert.True(IsRed(portrait, portrait.Width - 3, 2));
        Assert.False(IsRed(portrait, 2, 2));
        var tiny = Generate(folder, "photo.tiff", MediaKind.Photo)!;
        Assert.Equal((64, 48), (tiny.Width, tiny.Height));
    }

    [Fact]
    public void PngTransparencySurvivesAndHeicDecodesWithTheExtension()
    {
        using var folder = new TemporaryDirectory();
        var png = Generate(folder, "photo_alpha.png", MediaKind.Photo)!;
        Assert.Equal((320, 240), (png.Width, png.Height));
        Assert.True(png.HasAlpha);
        // HEIC needs the HEIF Image Extensions (installed on the dev PC); then WIC decodes it at size.
        if (Generate(folder, "photo.heic", MediaKind.Photo) is { } heic)
        {
            Assert.Equal((320, 240), (heic.Width, heic.Height));
        }
    }

    [Fact]
    public void VideoFramesAndCoverArtComeFromTheShell()
    {
        using var folder = new TemporaryDirectory();
        var clip = Generate(folder, "clip.mov", MediaKind.Video);
        Assert.NotNull(clip);
        Assert.True(clip.Width <= 320 && clip.Height <= 320);
        var cover = Generate(folder, "tone_with_cover.m4a", MediaKind.Audio);
        Assert.NotNull(cover);
        Assert.False(cover.HasAlpha);
        // No art, no thumbnail: the grid draws a kind glyph instead of a file icon.
        Assert.Null(Generate(folder, "tone_440_mono.wav", MediaKind.Audio));
    }

    [Fact]
    public void BrokenFilesHaveNoThumbnailAndStayUnlocked()
    {
        using var folder = new TemporaryDirectory();
        Assert.Null(Generate(folder, "corrupt_garbage.jpg", MediaKind.Photo));
        Assert.Null(Generate(folder, "empty.mp4", MediaKind.Video));
        Generate(folder, "photo_landscape.jpg", MediaKind.Photo);
        File.Delete(folder.Full("photo_landscape.jpg"));
        File.Delete(folder.Full("corrupt_garbage.jpg"));
    }

    // A 3 × 2 image whose pixels are numbered 1…6 row by row:
    //   1 2 3
    //   4 5 6
    [Theory]
    [InlineData(1, 3, 2, new uint[] { 1, 2, 3, 4, 5, 6 })]
    [InlineData(2, 3, 2, new uint[] { 3, 2, 1, 6, 5, 4 })]
    [InlineData(3, 3, 2, new uint[] { 6, 5, 4, 3, 2, 1 })]
    [InlineData(4, 3, 2, new uint[] { 4, 5, 6, 1, 2, 3 })]
    [InlineData(5, 2, 3, new uint[] { 1, 4, 2, 5, 3, 6 })]
    [InlineData(6, 2, 3, new uint[] { 4, 1, 5, 2, 6, 3 })]
    [InlineData(7, 2, 3, new uint[] { 6, 3, 5, 2, 4, 1 })]
    [InlineData(8, 2, 3, new uint[] { 3, 6, 2, 5, 1, 4 })]
    public void EveryExifOrientationTurnsThePixels(int orientation, int width, int height, uint[] expected)
    {
        var pixels = System.Runtime.InteropServices.MemoryMarshal.AsBytes(new uint[] { 1, 2, 3, 4, 5, 6 }.AsSpan()).ToArray();
        var (oriented, w, h) = WicPhotoDecoder.Orient(pixels, 3, 2, orientation);
        Assert.Equal((width, height), (w, h));
        Assert.Equal(expected, System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(oriented).ToArray());
    }
}