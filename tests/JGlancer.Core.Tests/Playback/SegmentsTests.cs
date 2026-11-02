using JGlancer.Core.Model;
using JGlancer.Core.Playback;

namespace JGlancer.Core.Tests.Playback;

public sealed class SegmentsTests
{
    [Fact]
    public void ANewSegmentIsATenthOfTheFileFromThePlayheadInsideTheFile()
    {
        Assert.Equal(new SegmentRange(10, 22), Segments.Default(120, 10));
        // 1 … 30 s, and never past the end.
        Assert.Equal(new SegmentRange(0, 1), Segments.Default(5, 0));
        Assert.Equal(30, Segments.Default(3600, 0).Length);
        Assert.Equal(new SegmentRange(108, 120), Segments.Default(120, 115));
        Assert.Equal(new SegmentRange(0, 0.5), Segments.Default(0.5, 0.2));
    }

    [Fact]
    public void OnlyCopyableVideosAndAnyAudioCanBeCut()
    {
        Assert.True(Segments.CanExport(@"C:\a\clip.MOV", MediaKind.Video));
        Assert.True(Segments.CanExport(@"C:\a\clip.mp4", MediaKind.Video));
        Assert.False(Segments.CanExport(@"C:\a\clip.mkv", MediaKind.Video));
        Assert.False(Segments.CanExport(@"C:\a\clip.webm", MediaKind.Video));
        Assert.True(Segments.CanExport(@"C:\a\song.ogg", MediaKind.Audio));
        Assert.False(Segments.CanExport(@"C:\a\photo.jpg", MediaKind.Photo));
    }

    [Fact]
    public void NamesCarryTheRangeTheSpeedAndTheKeptFormat()
    {
        var song = new SegmentExportRequest(@"C:\m\Song.flac", MediaKind.Audio, new SegmentRange(62, 75.44));
        Assert.Equal("Song (1m02.0s–1m15.4s).flac", Segments.SuggestedName(song));
        Assert.Equal("Song (1m02.0s–1m15.4s) 1.25x.m4a", Segments.SuggestedName(song with { Rate = 1.25, KeepsAudioFormat = false }));
        Assert.Equal("mp3", Segments.FileExtension(song with { Source = @"C:\m\Song.mp3" }));
        Assert.Equal("m4a", Segments.FileExtension(song with { Source = @"C:\m\Song.mp3", KeepsAudioFormat = false }));
        Assert.Equal("m4a", Segments.FileExtension(song with { Source = @"C:\m\Song.ogg" }));
        Assert.Equal("aiff", Segments.FileExtension(song with { Source = @"C:\m\Song.aifc" }));
        var clip = new SegmentExportRequest(@"C:\v\Clip.MOV", MediaKind.Video, new SegmentRange(0, 3), Rate: 2);
        Assert.Equal("Clip (0m00.0s–0m03.0s).mp4", Segments.SuggestedName(clip));
        Assert.Equal("1:02.0–1:15.4", Segments.Label(new SegmentRange(62, 75.44)));
    }

    [Fact]
    public void AnOccupiedNameGetsANumber()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\x\a.wav", @"C:\x\a 2.wav" };
        Assert.Equal(@"C:\x\a 3.wav", Segments.Vacant(@"C:\x\a.wav", taken.Contains));
        Assert.Equal(@"C:\x\b.wav", Segments.Vacant(@"C:\x\b.wav", taken.Contains));
    }
}
