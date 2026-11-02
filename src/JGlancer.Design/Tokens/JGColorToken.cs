using Avalonia.Media;

namespace JGlancer.Design;

/// <summary>
/// A semantic color with dark and light values and optional Increase Contrast variants (DESIGN.md §3, D-19).
/// Values are ARGB; alpha below 255 marks translucent tokens such as <c>overlay.scrim</c>.
/// </summary>
public sealed class JGColorToken
{
    public JGColorToken(string name, uint dark, uint light, uint? darkIncreasedContrast = null, uint? lightIncreasedContrast = null, uint? black = null)
    {
        Black = black;
        Name = name;
        Dark = dark;
        Light = light;
        DarkIncreasedContrast = darkIncreasedContrast;
        LightIncreasedContrast = lightIncreasedContrast;
        ResourceKey = "JG." + name;
    }

    /// <summary>The design name, e.g. <c>bg.base</c>.</summary>
    public string Name { get; }

    /// <summary>Key of the brush in the application resources, e.g. <c>JG.bg.base</c>.</summary>
    public string ResourceKey { get; }

    public uint Dark { get; }
    public uint Light { get; }
    public uint? DarkIncreasedContrast { get; }
    public uint? LightIncreasedContrast { get; }

    /// <summary>The value in the Black theme (W-97); a token without one takes the dark value.</summary>
    public uint? Black { get; }

    /// <summary>The ARGB value in the Black theme; Increase Contrast keeps the dark theme's contrast values.</summary>
    public uint BlackValue(bool increasedContrast) => (increasedContrast ? DarkIncreasedContrast : null) ?? Black ?? Dark;

    /// <summary>The ARGB value in one appearance; Increase Contrast falls back to the normal value.</summary>
    public uint Value(bool isDark, bool increasedContrast) => (isDark, increasedContrast) switch
    {
        (true, true) => DarkIncreasedContrast ?? Dark,
        (true, false) => Dark,
        (false, true) => LightIncreasedContrast ?? Light,
        (false, false) => Light,
    };

    /// <summary>The value as an Avalonia color.</summary>
    public Color Color(bool isDark, bool increasedContrast) => Avalonia.Media.Color.FromUInt32(Value(isDark, increasedContrast));

    public override string ToString() => Name;
}
