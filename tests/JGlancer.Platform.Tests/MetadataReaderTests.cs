using JGlancer.Core.Model;
using JGlancer.Core.Scanning;
using JGlancer.Core.Store;
using JGlancer.Core.Tests.Support;
using JGlancer.Platform.Windows;

namespace JGlancer.Platform.Tests;

public sealed class MetadataReaderTests
{
    private static MetadataUpdate Read(TemporaryDirectory folder, string name, MediaKind kind)
    {
        Fixtures.CopyTo(folder, name);
        return new WindowsMetadataReader().Read(new PendingMetadataItem(1, folder.Full(name), kind, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void PhotosGiveDisplaySizeOrientationAndCaptureDate()
    {
        using var folder = new TemporaryDirectory();
        var landscape = Read(folder, "photo_landscape.jpg", MediaKind.Photo);
        Assert.Equal(MetadataState.Extracted, landscape.State);
        Assert.Equal((640, 480, 1), (landscape.Width, landscape.Height, landscape.Orientation));
        // No offset in the file: the time is local, as on macOS.
        Assert.Equal(new DateTimeOffset(2024, 5, 17, 10, 30, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2024, 5, 17, 10, 30, 0))), landscape.CapturedAt);

        var portrait = Read(folder, "photo_orientation6.jpg", MediaKind.Photo);
        Assert.Equal((480, 640, 6), (portrait.Width, portrait.Height, portrait.Orientation));

        Assert.Equal((320, 240), Read(folder, "photo_alpha.png", MediaKind.Photo) is var png ? (png.Width, png.Height) : default);
        Assert.Equal((64, 48), Read(folder, "photo.tiff", MediaKind.Photo) is var tiff ? (tiff.Width, tiff.Height) : default);
    }

    [Fact]
    public void HeicNeedsTheHeifExtension()
    {
        using var folder = new TemporaryDirectory();
        var heic = Read(folder, "photo.heic", MediaKind.Photo);
        // With the HEIF Image Extension (installed on the dev PC) WIC reads it; without it the file is marked failed.
        Assert.Contains(heic.State, new[] { MetadataState.Extracted, MetadataState.Failed });
        if (heic.State == MetadataState.Extracted)
        {
            Assert.Equal((640, 480), (heic.Width, heic.Height));
            Assert.NotNull(heic.CapturedAt);
        }
    }

    [Fact]
    public void VideoAndAudioGiveDurationSizeRateAndCodec()
    {
        using var folder = new TemporaryDirectory();
        var clip = Read(folder, "clip.mov", MediaKind.Video);
        Assert.Equal(MetadataState.Extracted, clip.State);
        Assert.Equal((320, 240, "avc1"), (clip.Width, clip.Height, clip.Codec));
        Assert.Equal(2, clip.Duration!.Value, precision: 2);
        Assert.Equal(30, clip.FrameRate!.Value, precision: 2);

        // A phone-style portrait clip: landscape pixels shown rotated 90°.
        var portrait = Read(folder, "portrait_rotated.mov", MediaKind.Video);
        Assert.Equal((240, 320), (portrait.Width, portrait.Height));

        var tone = Read(folder, "tone_440_mono.wav", MediaKind.Audio);
        Assert.Equal(("lpcm", null), (tone.Codec, tone.Width));
        Assert.Equal(2, tone.Duration!.Value, precision: 2);
        Assert.Equal("aac", Read(folder, "tone_with_cover.m4a", MediaKind.Audio).Codec);
    }

    [Fact]
    public void UnreadableFilesFailWithoutThrowing()
    {
        using var folder = new TemporaryDirectory();
        Assert.Equal(MetadataState.Failed, Read(folder, "corrupt_garbage.jpg", MediaKind.Photo).State);
        Assert.Equal(MetadataState.Failed, Read(folder, "empty.mp4", MediaKind.Video).State);
        Assert.Equal(MetadataState.Failed, Read(folder, "unsupported.mkv", MediaKind.Video).State);
        Assert.Equal(MetadataState.Failed, new WindowsMetadataReader().Read(new PendingMetadataItem(1, folder.Full("gone.jpg"), MediaKind.Photo, default)).State);
    }

    [Fact]
    public void FilesAreNotLockedAfterReading()
    {
        using var folder = new TemporaryDirectory();
        Read(folder, "photo_landscape.jpg", MediaKind.Photo);
        Read(folder, "clip.mov", MediaKind.Video);
        // The user must be able to rename or delete a file right after the scan looked at it.
        // Both readers release their COM objects at once (unique wrappers), not at the next garbage collection.
        File.Move(folder.Full("photo_landscape.jpg"), folder.Full("renamed.jpg"));
        File.Delete(folder.Full("clip.mov"));
        Assert.False(File.Exists(folder.Full("clip.mov")));
    }
}
