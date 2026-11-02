using Avalonia;

namespace JGlancer.Design.Tests;

public sealed class ScrubberTests
{
    [Fact]
    public void TheWindowZoomsAroundTheAnchorDownToHalfASecondAndScrollsInsideTheFile()
    {
        var window = new JGScrubberWindow(100);
        Assert.False(window.IsZoomed);
        window.Zoom(4, 50);
        Assert.Equal((37.5, 25.0), (window.Start, window.Length));
        Assert.Equal(50, window.Time(window.X(50, 800), 800), 6);
        window.Zoom(1000, 50);
        Assert.Equal(JGScrubberWindow.MinimumLength, window.Length);
        window.Scroll(-1000);
        Assert.Equal(0, window.Start);
        window.Scroll(1000);
        Assert.Equal(100, window.End);
        window.Page(10);
        Assert.Equal(10 - 0.05, window.Start, 6);
        window.Zoom(0.001, 0);
        Assert.False(window.IsZoomed);
    }

    [Fact]
    public void TheWaveformFillsTheHeightAtItsLoudestAndKeepsSilenceVisible()
    {
        var loud = JGScrubberMath.WaveformOutline(new JGWaveformPeaks([-0.5f, -0.25f], [0.5f, 0.25f]), new Size(100, 40));
        Assert.Equal(200, loud.Count);
        Assert.Equal(0, loud.Min(p => p.Y), 3);
        Assert.Equal(40, loud.Max(p => p.Y), 3);
        var silence = JGScrubberMath.WaveformOutline(new JGWaveformPeaks([0f], [0f]), new Size(100, 40));
        Assert.True(silence.Max(p => p.Y) - silence.Min(p => p.Y) > 0);
    }
}
