using JGlancer.Core.Model;
using JGlancer.Core.Playback;

namespace JGlancer.Core.Tests.Playback;

public sealed class QueueAndSpeedTests
{
    private static List<PlaybackItem> Items(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new PlaybackItem($@"C:\tmp\{i}.mp3", $"{i}.mp3", MediaKind.Audio))];

    [Fact]
    public void ClampsTheStartIndexAndKnowsItsNeighbors()
    {
        var queue = new PlaybackQueue(Items(3), 7);
        Assert.Equal(2, queue.Index);
        Assert.True(queue.HasPrevious);
        Assert.False(queue.HasNext);
        Assert.Null(PlaybackQueue.Empty.Current);
        Assert.Equal(0, new PlaybackQueue(Items(3), -1).Index);
    }

    [Fact]
    public void SelectRejectsOutOfRangeAndCurrent()
    {
        var queue = new PlaybackQueue(Items(3), 1);
        Assert.Null(queue.Select(1));
        Assert.Null(queue.Select(3));
        Assert.Equal("2.mp3", queue.Select(2)?.Current?.Name);
    }

    [Fact]
    public void EndActionLoopsAdvancesOrStops()
    {
        var middle = new PlaybackQueue(Items(3), 1);
        Assert.IsType<EndAction.Replay>(middle.EndAction(loop: true, autoAdvance: true));
        Assert.Equal(new EndAction.Advance(2), middle.EndAction(loop: false, autoAdvance: true));
        Assert.IsType<EndAction.StayAtEnd>(middle.EndAction(loop: false, autoAdvance: false));
        var last = new PlaybackQueue(Items(3), 2);
        Assert.IsType<EndAction.StayAtEnd>(last.EndAction(loop: false, autoAdvance: true));
        Assert.IsType<EndAction.Replay>(last.EndAction(loop: true, autoAdvance: false));
    }

    [Fact]
    public void PreviousRestartsAfterTheThresholdOrAtTheFirstItem()
    {
        var middle = new PlaybackQueue(Items(3), 1);
        Assert.Equal(new PreviousAction.Move(0), middle.PreviousAction(1));
        Assert.IsType<PreviousAction.Restart>(middle.PreviousAction(PlaybackQueue.RestartThreshold + 0.1));
        Assert.IsType<PreviousAction.Restart>(new PlaybackQueue(Items(3), 0).PreviousAction(0));
    }

    [Fact]
    public void ReplacingItemsKeepsTheCurrentOneAndRemovingMovesOn()
    {
        var queue = PlaybackQueue.Keeping(Items(3), @"C:\tmp\1.mp3");
        Assert.Equal(1, queue.Index);
        Assert.Equal(0, PlaybackQueue.Keeping(Items(2), @"C:\tmp\gone.mp3").Index);
        var removed = queue.RemovingCurrent();
        Assert.Equal(2, removed.Count);
        Assert.Equal("2.mp3", removed.Current?.Name);
        Assert.Equal("1.mp3", new PlaybackQueue(Items(3), 2).RemovingCurrent().Current?.Name);
    }

    [Fact]
    public void SpeedPresetsAreAscendingInsideTheRangeAndIncludeNormal()
    {
        Assert.Equal(PlaybackSpeed.Presets.Order(), PlaybackSpeed.Presets);
        Assert.Contains(PlaybackSpeed.Normal, PlaybackSpeed.Presets);
        Assert.All(PlaybackSpeed.Presets, rate => Assert.InRange(rate, PlaybackSpeed.Minimum, PlaybackSpeed.Maximum));
        Assert.Equal([0.5, 0.75, 1, 1.1, 1.25, 1.5, 2], PlaybackSpeed.Choices(1.1));
    }

    [Fact]
    public void SpeedClampsToTheRangeAndTheFineStepWithoutDrift()
    {
        Assert.Equal(PlaybackSpeed.Maximum, PlaybackSpeed.Clamped(10));
        Assert.Equal(PlaybackSpeed.Minimum, PlaybackSpeed.Clamped(0));
        Assert.Equal(1.25, PlaybackSpeed.Clamped(1.234));
        var rate = PlaybackSpeed.Normal;
        for (var i = 0; i < 5; i++)
        {
            rate = PlaybackSpeed.Stepped(rate, 1);
        }
        Assert.Equal(1.25, rate);
        Assert.All(PlaybackSpeed.Presets, preset => Assert.Equal(preset, PlaybackSpeed.Clamped(preset)));
        for (var i = 0; i < 100; i++)
        {
            rate = PlaybackSpeed.Stepped(rate, -1);
        }
        Assert.Equal(PlaybackSpeed.Minimum, rate);
    }
}
