namespace JGlancer.Design;

/// <summary>A color offered in Settings → Appearance → Accent Color; <see cref="Name"/> is the English key of its name.</summary>
public sealed record JGAccentPreset(string Name, uint Color);

/// <summary>
/// The accent the user chooses (W-93, W-98): one color, shown exactly in every theme on fills and edges. Where it is
/// text or a thin glyph (<c>accent.text</c>) only its lightness moves, by the smallest step that keeps it readable on that
/// theme's surfaces. The default color gives the token values themselves.
/// </summary>
public static class JGAccent
{
    /// <summary>The default accent, close to the blue of the app icon's wave.</summary>
    public const uint DefaultColor = 0xFF1F3BFF;

    /// <summary>The side of a color swatch in Settings.</summary>
    public const double SwatchSize = 20;

    private const double LightnessStep = 0.01;
    private const double DimLightnessDark = 0.22;
    private const double DimLightnessLight = 0.92;
    private const double DimSaturationDark = 0.45;
    private const double DimSaturationLight = 0.75;

    /// <summary>The colors offered in Settings, the default first.</summary>
    public static IReadOnlyList<JGAccentPreset> Presets { get; } =
    [
        new("Blue", DefaultColor),
        new("Lavender", 0xFFA396F4),
        new("Purple", 0xFF7A2CFF),
        new("Pink", 0xFFFF2E93),
        new("Red", 0xFFE60012),
        new("Orange", 0xFFFF4F1A),
        new("Yellow", 0xFFF5B800),
        new("Green", 0xFF18A558),
        new("Teal", 0xFF00A6C8),
    ];

    /// <summary>The <c>accent.text</c> value of <paramref name="chosen"/> in one appearance.</summary>
    public static uint Text(uint chosen, bool isDark) => Adjusted(chosen, token => token.Value(isDark, false), lighter: isDark);

    /// <summary>The <c>accent.text</c> value of <paramref name="chosen"/> in the Black theme.</summary>
    public static uint TextBlack(uint chosen) => Adjusted(chosen, token => token.BlackValue(false), lighter: true);

    /// <summary>White or black, whichever reads better on <paramref name="accent"/> (a tick, a switch's knob).</summary>
    public static uint On(uint accent) =>
        JGContrast.Ratio(0xFFFFFFFF, accent) >= JGContrast.Ratio(0xFF000000, accent) ? 0xFFFFFFFF : 0xFF000000;

    private static uint Adjusted(uint chosen, Func<JGColorToken, uint> surface, bool lighter)
    {
        if (MeetsContrast(chosen, surface))
        {
            return chosen;
        }
        var (hue, saturation, lightness) = ToHsl(chosen);
        var step = lighter ? LightnessStep : -LightnessStep;
        for (lightness += step; lightness is > 0 and < 1; lightness += step)
        {
            var moved = FromHsl(hue, saturation, lightness);
            if (MeetsContrast(moved, surface))
            {
                return moved;
            }
        }
        return lighter ? 0xFFFFFFFF : 0xFF000000;
    }

    /// <summary>The <c>accent.dim</c> value (selection fill) of <paramref name="chosen"/> in one appearance.</summary>
    public static uint Dim(uint chosen, bool isDark)
    {
        var (hue, saturation, _) = ToHsl(chosen);
        return isDark
            ? FromHsl(hue, Math.Min(saturation, DimSaturationDark), DimLightnessDark)
            : FromHsl(hue, Math.Min(saturation, DimSaturationLight), DimLightnessLight);
    }

    /// <summary>Text on the base and panel surfaces, graphics on every surface (JGContrastRequirement).</summary>
    private static bool MeetsContrast(uint color, Func<JGColorToken, uint> surface)
    {
        const double text = 4.5;
        const double graphic = 3.0;
        return JGContrast.Ratio(color, surface(JGColor.BgBase)) >= text
            && JGContrast.Ratio(color, surface(JGColor.BgPanel)) >= text
            && JGContrast.Ratio(color, surface(JGColor.BgRaised)) >= graphic
            && JGContrast.Ratio(color, surface(JGColor.BgMedia)) >= graphic;
    }

    private static (double Hue, double Saturation, double Lightness) ToHsl(uint argb)
    {
        var r = ((argb >> 16) & 0xFF) / 255.0;
        var g = ((argb >> 8) & 0xFF) / 255.0;
        var b = (argb & 0xFF) / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        var chroma = max - min;
        if (chroma == 0)
        {
            return (0, 0, lightness);
        }
        var saturation = chroma / (1 - Math.Abs(2 * lightness - 1));
        var hue = max == r ? (g - b) / chroma : max == g ? (b - r) / chroma + 2 : (r - g) / chroma + 4;
        return ((hue + 6) % 6 / 6, saturation, lightness);
    }

    private static uint FromHsl(double hue, double saturation, double lightness)
    {
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        uint Channel(double offset)
        {
            var k = (offset + hue * 12) % 12;
            var value = lightness - chroma / 2 * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1));
            return (uint)Math.Clamp(Math.Round(value * 255), 0, 255);
        }
        return 0xFF000000u | (Channel(0) << 16) | (Channel(8) << 8) | Channel(4);
    }
}
