using JGlancer.Core.Model;
using JGlancer.Core.Playback;

namespace JGlancer.Core.Tests.Playback;

public sealed class PlaybackPreferencesTests
{
    [Fact]
    public void DefaultsPlayMusicOnAndStopVideos()
    {
        var preferences = new PlaybackPreferences();
        Assert.True(preferences.PlaysNext(MediaKind.Audio));
        Assert.False(preferences.PlaysNext(MediaKind.Video));
        Assert.Equal(1, preferences.Volume);
        Assert.True(preferences.PreservesPitch);
    }

    [Fact]
    public void RoundTripsPerKindAndRepairsBadFiles()
    {
        var preferences = new PlaybackPreferences { Volume = 0.4, PreservesPitch = false };
        preferences.SetRate(1.5, MediaKind.Video);
        preferences.SetPlaysNext(true, MediaKind.Video);
        var read = PlaybackPreferences.FromJson(preferences.ToJson());
        Assert.Equal(preferences, read);
        Assert.Equal(1.5, read.Rate(MediaKind.Video));
        Assert.Equal(1, read.Rate(MediaKind.Audio));
        // Older files lack keys; broken ones give defaults; values out of range are clamped.
        Assert.True(PlaybackPreferences.FromJson("""{"volume":0.5}""").AudioPlaysNext);
        Assert.Equal(new PlaybackPreferences(), PlaybackPreferences.FromJson("{not json"));
        var wild = PlaybackPreferences.FromJson("""{"videoRate":40,"volume":7}""");
        Assert.Equal((PlaybackSpeed.Maximum, 1.0), (wild.VideoRate, wild.Volume));
    }
}
