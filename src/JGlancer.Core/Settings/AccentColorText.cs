using System.Globalization;

namespace JGlancer.Core.Settings;

/// <summary>The accent color as it is saved and typed: <c>#RRGGBB</c> (W-93).</summary>
public static class AccentColorText
{
    private const int HexDigits = 6;
    private const uint Opaque = 0xFF000000;

    /// <summary>Reads <c>#RRGGBB</c> or <c>RRGGBB</c> (any case, spaces around) into an opaque ARGB value.</summary>
    public static bool TryParse(string? text, out uint argb)
    {
        argb = 0;
        var digits = text?.Trim().TrimStart('#');
        if (digits is not { Length: HexDigits } || !digits.All(char.IsAsciiHexDigit))
        {
            return false;
        }
        argb = Opaque | uint.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>The <c>#RRGGBB</c> of an ARGB value; alpha is dropped.</summary>
    public static string Format(uint argb) => string.Create(CultureInfo.InvariantCulture, $"#{argb & ~Opaque:X6}");
}
