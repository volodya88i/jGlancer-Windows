using JGlancer.Core.Model;

namespace JGlancer.Core.Tests.Model;

public sealed class PagerAndSwipeTests
{
    private static readonly Func<int, bool> Both = _ => true;

    [Fact]
    public void WindowIsCurrentThenNearestNeighbors()
    {
        Assert.Equal([5, 6, 4, 7, 3], Pager.Window(5, 10));
        Assert.Equal([0, 1, 2], Pager.Window(0, 10));
        Assert.Equal([9, 8, 7], Pager.Window(9, 10));
        Assert.Equal([0], Pager.Window(0, 1));
        Assert.Empty(Pager.Window(3, 0));
    }

    [Fact]
    public void MovesClampWithoutWrapping()
    {
        Assert.Equal(0, Pager.Move(0, -1, 5));
        Assert.Equal(4, Pager.Move(4, 1, 5));
        Assert.Equal(3, Pager.Move(2, 1, 5));
        Assert.Equal(0, Pager.Move(0, 1, 0));
    }

    [Fact]
    public void InstantPagesOncePerGestureAfterTheThreshold()
    {
        var tracker = new SwipeTracker(isAnimated: false);
        Assert.Null(tracker.Move(-30, Both));
        Assert.Equal(1, tracker.Move(-20, Both));
        // The same gesture does not page again.
        Assert.Null(tracker.Move(-200, Both));
        Assert.Null(tracker.End(800, 0, Both));
        // Content to the right brings the previous file; a missing neighbor does not page.
        Assert.Equal(-1, tracker.Move(50, Both));
        tracker.End(800, 0, Both);
        Assert.Null(tracker.Move(50, direction => direction == 1));
    }

    [Fact]
    public void AnimatedCompletesWhenFarOrFlungAndSpringsBackOtherwise()
    {
        var tracker = new SwipeTracker(isAnimated: true);
        Assert.Null(tracker.Move(-100, Both));
        Assert.Equal(-100, tracker.Offset(Both));
        // 100 of 800 is short and slow: back.
        Assert.Null(tracker.End(800, -100, Both));
        tracker.Move(-200, Both);
        Assert.Equal(1, tracker.End(800, 0, Both));
        // A short flick completes; a flick against the travel does not.
        tracker.Move(60, Both);
        Assert.Equal(-1, tracker.End(800, 900, Both));
        tracker.Move(60, Both);
        Assert.Null(tracker.End(800, -900, Both));
    }

    [Fact]
    public void NoNeighborMeansResistanceAndNoPage()
    {
        var tracker = new SwipeTracker(isAnimated: true);
        Func<int, bool> onlyPrevious = direction => direction == -1;
        tracker.Move(-400, onlyPrevious);
        Assert.Equal(-400 * SwipeTracker.EdgeResistance, tracker.Offset(onlyPrevious));
        Assert.Null(tracker.End(800, -2000, onlyPrevious));
    }
}
