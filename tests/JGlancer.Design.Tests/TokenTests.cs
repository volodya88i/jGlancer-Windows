using JGlancer.Tools;

namespace JGlancer.Design.Tests;

public sealed class TokenTests
{
    public static TheoryData<bool, bool> Appearances => new() { { true, false }, { false, false }, { true, true }, { false, true } };

    /// <summary>The macOS contrast policy (D-19): text ≥ 4.5:1, graphics ≥ 3:1, in every appearance.</summary>
    [Theory]
    [MemberData(nameof(Appearances))]
    public void EveryRequiredPairMeetsItsMinimum(bool isDark, bool increasedContrast)
    {
        var failures = JGContrastRequirement.All
            .Where(pair => increasedContrast || !pair.IncreasedContrastOnly)
            .Select(pair => (pair, ratio: pair.Ratio(isDark, increasedContrast)))
            .Where(x => x.ratio < x.pair.MinimumRatio)
            .Select(x => $"{x.pair.Foreground} on {x.pair.Background}: {x.ratio:0.00} < {x.pair.MinimumRatio}")
            .ToList();
        Assert.Empty(failures);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryRequiredPairMeetsItsMinimumInBlack(bool increasedContrast)
    {
        var failures = JGContrastRequirement.All
            .Where(pair => increasedContrast || !pair.IncreasedContrastOnly)
            .Select(pair => (pair, ratio: JGContrast.Ratio(pair.Foreground.BlackValue(increasedContrast), pair.Background.BlackValue(increasedContrast))))
            .Where(x => x.ratio < x.pair.MinimumRatio)
            .Select(x => $"{x.pair.Foreground} on {x.pair.Background}: {x.ratio:0.00} < {x.pair.MinimumRatio}")
            .ToList();
        Assert.Empty(failures);
    }

    [Fact]
    public void ContrastRatioMatchesWcagReferenceValues()
    {
        Assert.Equal(21, JGContrast.Ratio(0xFF000000, 0xFFFFFFFF), precision: 2);
        Assert.Equal(1, JGContrast.Ratio(0xFF777777, 0xFF777777), precision: 2);
        Assert.Equal(4.48, JGContrast.Ratio(0xFF777777, 0xFFFFFFFF), precision: 2);
    }

    [Fact]
    public void GeneratedTokensMatchTheExportedDesignTokens()
    {
        var root = RepoPaths.FindRoot();
        var expected = TokenGenerator.Generate(File.ReadAllText(RepoPaths.TokensInput(root))).ReplaceLineEndings("\n");
        var actual = File.ReadAllText(RepoPaths.TokensOutput(root)).ReplaceLineEndings("\n");
        Assert.True(expected == actual, "JGTokens.g.cs is stale: run `dotnet run --project tools/JGlancer.Tools -- tokens`");
    }

    [Fact]
    public void TokensKeepTheirMacOSValues()
    {
        Assert.Equal(0xFF121113u, JGColor.BgBase.Dark);
        Assert.Equal(JGAccent.DefaultColor, JGColor.Accent.Light); // The icon's blue, not macOS' violet (W-93) …
        Assert.Equal(JGAccent.DefaultColor, JGColor.Accent.Dark); // … exact in every theme (W-98).
        Assert.Equal(JGAccent.DefaultColor, JGColor.Accent.BlackValue(false));
        Assert.Equal(0xB8121113u, JGColor.OverlayScrim.Dark); // 72 % alpha
        Assert.Equal(220, JGMetrics.SidebarWidth);
        Assert.Equal(32, JGTypeScale.Display.Size);
        Assert.Equal(-0.96, JGTypeScale.Display.LetterSpacing, 6);
    }

    [Fact]
    public void IncreasedContrastFallsBackToTheNormalValue()
    {
        Assert.Equal(JGColor.Accent.Dark, JGColor.Accent.Value(isDark: true, increasedContrast: true));
        Assert.Equal(0xFF77717Au, JGColor.LineHair.Value(isDark: true, increasedContrast: true));
    }

    [Fact]
    public void MinimumTextSizeIsElevenPoints() =>
        Assert.All(JGTypeScale.All, style => Assert.True(style.Size >= 11, style.Name)); // macOS D-18

    [Fact]
    public void ResourceKeysAreUnique() =>
        Assert.Equal(JGColor.All.Count, JGColor.All.Select(t => t.ResourceKey).Distinct().Count());
}
