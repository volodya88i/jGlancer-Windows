using Avalonia.Media;

namespace JGlancer.Design;

/// <summary>The bundled font families (W-03). Nothing is loaded from the network or the system font list.</summary>
public static class JGFonts
{
    private const string Assets = "avares://JGlancer.Design/Assets/Fonts";

    /// <summary>Inter from Avalonia.Fonts.Inter (registered by <c>AppBuilder.WithInterFont()</c>).</summary>
    public static FontFamily Text { get; } = new("fonts:Inter#Inter");

    /// <summary>Unbounded SemiBold / ExtraBold, static instances (Avalonia does not apply variable-font axes).</summary>
    public static FontFamily Display { get; } = new($"{Assets}#Unbounded");

    /// <summary>JetBrains Mono Regular / Medium.</summary>
    public static FontFamily Mono { get; } = new($"{Assets}#JetBrains Mono");

    /// <summary>Windows caption glyphs (minimize, maximize, restore, close) as in native title bars.</summary>
    public static FontFamily Caption { get; } = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static FontFamily For(JGFontFamily family) => family switch
    {
        JGFontFamily.Display => Display,
        JGFontFamily.Mono => Mono,
        _ => Text,
    };
}
