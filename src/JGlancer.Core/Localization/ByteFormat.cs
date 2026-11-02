using System.Globalization;

namespace JGlancer.Core.Localization;

/// <summary>
/// File sizes as the macOS app shows them (ByteCountFormatter, file style): decimal units, whole kilobytes, one
/// decimal from megabytes on, localized number and unit — "203 KB", "140,3 MB", "Zero KB".
/// </summary>
public static class ByteFormat
{
    public static string Format(long bytes)
    {
        if (bytes <= 0)
        {
            return L.T("Zero KB");
        }
        if (bytes < 1000)
        {
            return L.Plural("{0} bytes", bytes);
        }
        var culture = CultureInfo.CurrentCulture;
        string[] units = ["{0} KB", "{0} MB", "{0} GB", "{0} TB"];
        var value = bytes / 1000d;
        var unit = 0;
        while (value >= 999.95 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }
        var number = unit == 0
            ? Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", culture)
            : value.ToString("0.#", culture);
        return L.T(units[unit], number);
    }
}
