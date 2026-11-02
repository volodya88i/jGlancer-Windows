namespace JGlancer.Design;

/// <summary>WCAG 2 relative luminance and contrast ratio.</summary>
public static class JGContrast
{
    /// <summary>The contrast ratio (1…21) of two opaque ARGB colors; alpha is ignored.</summary>
    public static double Ratio(uint first, uint second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>Relative luminance of an sRGB color.</summary>
    public static double Luminance(uint argb)
    {
        static double Channel(uint value)
        {
            var c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel((argb >> 16) & 0xFF) + 0.7152 * Channel((argb >> 8) & 0xFF) + 0.0722 * Channel(argb & 0xFF);
    }
}

/// <summary>A foreground/background token pair that must meet a WCAG minimum (macOS JGContrastRequirement).</summary>
public sealed record JGContrastRequirement(JGColorToken Foreground, JGColorToken Background, JGContrastRole Role, bool IncreasedContrastOnly)
{
    /// <summary>4.5:1 for text, 3:1 for graphics.</summary>
    public double MinimumRatio => Role == JGContrastRole.Text ? 4.5 : 3.0;

    public double Ratio(bool isDark, bool increasedContrast) =>
        JGContrast.Ratio(Foreground.Value(isDark, increasedContrast), Background.Value(isDark, increasedContrast));

    /// <summary>Every token pair the design uses that has a contrast obligation (DESIGN.md §3, D-19).</summary>
    public static IReadOnlyList<JGContrastRequirement> All { get; } = Build();

    private static List<JGContrastRequirement> Build()
    {
        var pairs = new List<JGContrastRequirement>();
        void Add(JGColorToken foreground, JGColorToken[] backgrounds, JGContrastRole role, bool increasedContrastOnly = false)
        {
            foreach (var background in backgrounds)
            {
                pairs.Add(new JGContrastRequirement(foreground, background, role, increasedContrastOnly));
            }
        }
        JGColorToken[] surfaces = [JGColor.BgBase, JGColor.BgPanel, JGColor.BgRaised, JGColor.BgPopup];
        Add(JGColor.TextPrimary, [.. surfaces, JGColor.BgMedia, JGColor.AccentDim], JGContrastRole.Text);
        Add(JGColor.TextSecondary, surfaces, JGContrastRole.Text);
        Add(JGColor.TextTertiary, surfaces, JGContrastRole.Text);
        // Primary button: background-colored label on a text.primary block.
        Add(JGColor.BgBase, [JGColor.TextPrimary], JGContrastRole.Text);
        foreach (var status in new[] { JGColor.AccentText, JGColor.Danger, JGColor.Success, JGColor.Warning })
        {
            Add(status, [JGColor.BgBase, JGColor.BgPanel], JGContrastRole.Text);
        }
        // The accent itself is exactly the color chosen (W-98); accent.text carries the obligation.
        Add(JGColor.AccentText, [.. surfaces, JGColor.BgMedia], JGContrastRole.Graphic);
        // Players: artist and album under the title, unplayed waveform.
        Add(JGColor.TextSecondary, [JGColor.BgMedia], JGContrastRole.Text);
        Add(JGColor.TextTertiary, [JGColor.BgMedia], JGContrastRole.Graphic);
        Add(JGColor.LineHair, surfaces, JGContrastRole.Graphic, increasedContrastOnly: true);
        Add(JGColor.LineStrong, surfaces, JGContrastRole.Graphic, increasedContrastOnly: true);
        return pairs;
    }
}

public enum JGContrastRole
{
    /// <summary>At least 4.5:1.</summary>
    Text,

    /// <summary>Icons, selection edges, meaningful lines: at least 3:1.</summary>
    Graphic,
}
