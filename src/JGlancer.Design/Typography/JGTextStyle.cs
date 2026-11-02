using Avalonia.Media;

namespace JGlancer.Design;

/// <summary>Which bundled family a text style uses (W-03).</summary>
public enum JGFontFamily
{
    /// <summary>Inter: body, buttons, labels.</summary>
    Text,

    /// <summary>Unbounded: the wide display and heading styles (macOS: SF Pro Expanded).</summary>
    Display,

    /// <summary>JetBrains Mono: sizes, counts, time codes.</summary>
    Mono,
}

/// <summary>One of the six text styles (DESIGN.md §4). Tracking is a fraction of the font size.</summary>
public sealed class JGTextStyle(string name, double size, int weight, JGFontFamily family, double tracking, bool uppercase)
{
    public string Name { get; } = name;
    public double Size { get; } = size;
    public FontWeight Weight { get; } = (FontWeight)weight;
    public JGFontFamily Family { get; } = family;
    public double Tracking { get; } = tracking;
    public bool Uppercase { get; } = uppercase;

    /// <summary>Letter spacing in pixels for Avalonia's <c>LetterSpacing</c>.</summary>
    public double LetterSpacing => Math.Round(Tracking * Size, 2);

    /// <summary>The CSS-like class that applies this style to a TextBlock (<c>Classes="body"</c>).</summary>
    public string ClassName => Name;
}
