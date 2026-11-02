namespace JGlancer.Design.Tests;

public sealed class AccentTests
{
    /// <summary>Every color the wheel can give, roughly: the presets, grays and a grid over RGB.</summary>
    public static TheoryData<uint> Colors()
    {
        var colors = new TheoryData<uint>();
        foreach (var preset in JGAccent.Presets)
        {
            colors.Add(preset.Color);
        }
        for (uint r = 0; r <= 255; r += 51)
        {
            for (uint g = 0; g <= 255; g += 51)
            {
                for (uint b = 0; b <= 255; b += 51)
                {
                    colors.Add(0xFF000000 | (r << 16) | (g << 8) | b);
                }
            }
        }
        return colors;
    }

    [Theory]
    [MemberData(nameof(Colors))]
    public void AnyChosenColorKeepsTheContrastMinimums(uint chosen)
    {
        foreach (var isDark in new[] { true, false })
        {
            var accent = JGAccent.Text(chosen, isDark);
            Assert.True(JGContrast.Ratio(accent, JGColor.BgBase.Value(isDark, false)) >= 4.5);
            Assert.True(JGContrast.Ratio(accent, JGColor.BgPanel.Value(isDark, false)) >= 4.5);
            Assert.True(JGContrast.Ratio(accent, JGColor.BgRaised.Value(isDark, false)) >= 3);
            Assert.True(JGContrast.Ratio(accent, JGColor.BgMedia.Value(isDark, false)) >= 3);
            Assert.True(JGContrast.Ratio(JGColor.TextPrimary.Value(isDark, false), JGAccent.Dim(chosen, isDark)) >= 4.5);
        }
        var black = JGAccent.TextBlack(chosen);
        Assert.True(JGContrast.Ratio(black, JGColor.BgBase.BlackValue(false)) >= 4.5);
        Assert.True(JGContrast.Ratio(black, JGColor.BgRaised.BlackValue(false)) >= 3);
    }

    [Fact]
    public void TheDefaultColorGivesTheTokenValues()
    {
        Assert.Equal(JGColor.AccentText.Light, JGAccent.Text(JGAccent.DefaultColor, isDark: false));
        Assert.Equal(JGColor.AccentText.Dark, JGAccent.Text(JGAccent.DefaultColor, isDark: true));
        Assert.Equal(JGColor.AccentDim.Light, JGAccent.Dim(JGAccent.DefaultColor, isDark: false));
        Assert.Equal(JGColor.AccentDim.Dark, JGAccent.Dim(JGAccent.DefaultColor, isDark: true));
        Assert.Equal(JGColor.AccentText.BlackValue(false), JGAccent.TextBlack(JGAccent.DefaultColor));
    }

    [Fact]
    public void AColorThatMeetsTheMinimumsStaysExact()
    {
        Assert.Equal(0xFF1F3BFFu, JGAccent.Text(0xFF1F3BFF, isDark: false));
        Assert.Equal(0xFFA396F4u, JGAccent.Text(0xFFA396F4, isDark: true));
    }
}
