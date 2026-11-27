using Avalonia.Media;
using JGlancer.Core.Model;

namespace JGlancer.App.SizeMap;

/// <summary>
/// The size map's file colors (macOS D-127, MAC-048): a family per kind — photos blue, videos violet, audio amber — and
/// within it one of four shades per extension (JPG and HEIC tell apart), calm enough for the app's neutrals, lighter in
/// the light themes.
/// </summary>
internal static class SizeMapPalette
{
    private const int Shades = 4;

    /// <summary>Hues of the kinds, in degrees.</summary>
    private static double Hue(MediaKind kind) => kind switch
    {
        MediaKind.Video => 268,
        MediaKind.Audio => 34,
        _ => 212,
    };

    public static Color FileColor(MediaKind kind, string extension, bool isDark)
    {
        // A stable shade for the extension (string.GetHashCode changes between launches): the Mac's hash.
        var hash = 0;
        foreach (var rune in extension.EnumerateRunes())
        {
            hash = unchecked(hash * 31 + rune.Value) & 0xFFFF;
        }
        var shade = hash % Shades;
        var lightness = (isDark ? 0.40 : 0.58) + shade * 0.06;
        return Hsl(Hue(kind), isDark ? 0.42 : 0.48, lightness);
    }

    /// <summary>The names on files: white 88 % in the dark themes, black 72 % in the light ones.</summary>
    public static Color FileInk(bool isDark) => isDark ? Color.FromArgb(224, 255, 255, 255) : Color.FromArgb(184, 0, 0, 0);

    public static Color Hsl(double hue, double saturation, double lightness)
    {
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var segment = hue / 60;
        var second = chroma * (1 - Math.Abs(segment % 2 - 1));
        var match = lightness - chroma / 2;
        var (r, g, b) = ((int)segment % 6) switch
        {
            0 => (chroma, second, 0d),
            1 => (second, chroma, 0d),
            2 => (0d, chroma, second),
            3 => (0d, second, chroma),
            4 => (second, 0d, chroma),
            _ => (chroma, 0d, second),
        };
        static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
        return Color.FromRgb(Channel(r + match), Channel(g + match), Channel(b + match));
    }
}
