using JGlancer.Core.Launch;

namespace JGlancer.Core.Tests.Launch;

public sealed class LaunchArgumentsTests
{
    private static readonly HashSet<string> Values = new(StringComparer.OrdinalIgnoreCase) { "--data-dir", "--snapshot" };

    [Fact]
    public void PathsPreviewAndBackgroundAreTold()
    {
        var launch = LaunchArguments.Parse(["--data-dir", @"C:\data", @"C:\a b\photo.jpg", "--select-first", @"C:\folder"], Values);
        Assert.Equal([@"C:\a b\photo.jpg", @"C:\folder"], launch.Paths);
        Assert.Null(launch.Preview);
        Assert.False(launch.Background);

        var preview = LaunchArguments.Parse(["--preview", @"""C:\x\song.mp3"""], Values);
        Assert.Equal(@"C:\x\song.mp3", preview.Preview);
        Assert.Empty(preview.Paths);
        Assert.True(LaunchArguments.Parse(["--background"], Values).Background);
        Assert.True(LaunchArguments.Parse(["--snapshot", @"C:\out.png"], Values).IsEmpty);
    }

    [Fact]
    public void ARunningAppGetsTheSameRequest()
    {
        var launch = new LaunchArguments([@"C:\a.jpg", @"C:\Фото\b.png"], @"C:\c.mp4", true);
        var back = LaunchArguments.Deserialize(launch.Serialize());
        Assert.Equal(launch.Paths, back.Paths);
        Assert.Equal(launch.Preview, back.Preview);
        Assert.True(back.Background);
        Assert.True(LaunchArguments.Deserialize("").IsEmpty);
    }
}
