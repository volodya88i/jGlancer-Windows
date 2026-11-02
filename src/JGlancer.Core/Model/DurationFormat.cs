using System.Globalization;

namespace JGlancer.Core.Model;

/// <summary>Time codes for badges, scrubbers and players: <c>0:42</c>, <c>12:05</c>, <c>1:02:03</c>.</summary>
public static class DurationFormat
{
    /// <summary>Positional time with no leading zero on the first unit; null for negative or non-finite input.</summary>
    public static string? Positional(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            return null;
        }
        var total = (long)Math.Round(seconds, MidpointRounding.AwayFromZero);
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var remainder = total % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{remainder:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{remainder:00}");
    }

    /// <summary>Positional time with tenths (<c>1:02.3</c>), for segment ends; null for negative or non-finite input.</summary>
    public static string? Tenths(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            return null;
        }
        var tenths = (long)Math.Round(seconds * 10, MidpointRounding.AwayFromZero);
        return Positional(tenths / 10) is { } whole ? string.Create(CultureInfo.InvariantCulture, $"{whole}.{tenths % 10}") : null;
    }
}
