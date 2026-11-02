using System.Globalization;

namespace JGlancer.Core.Scanning;

/// <summary>EXIF date text (<c>yyyy:MM:dd HH:mm:ss</c>) with an optional <c>±HH:mm</c> offset (macOS MetadataExtractor).</summary>
public static class ExifDate
{
    /// <summary>
    /// Parses <paramref name="text"/>; without an offset the time is taken as local in <paramref name="defaultZone"/>
    /// (the machine's zone when null). Null for zero dates and garbage.
    /// </summary>
    public static DateTimeOffset? Parse(string? text, string? offset, TimeZoneInfo? defaultZone = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var numbers = text.Split([':', ' ', '-', 'T'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : -1)
            .ToList();
        if (numbers.Count < 6 || numbers.Take(6).Any(n => n < 0) || numbers[0] <= 0 || numbers[1] is < 1 or > 12 || numbers[2] is < 1 or > 31)
        {
            return null;
        }
        DateTime local;
        try
        {
            local = new DateTime(numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5], DateTimeKind.Unspecified);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        if (ParseOffset(offset) is { } explicitOffset)
        {
            return new DateTimeOffset(local, explicitOffset);
        }
        var zone = defaultZone ?? TimeZoneInfo.Local;
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static TimeSpan? ParseOffset(string? offset)
    {
        if (offset is not { Length: >= 6 } || offset[0] is not ('+' or '-'))
        {
            return null;
        }
        var parts = offset[1..].Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || hours > 14 || minutes > 59)
        {
            return null;
        }
        var span = new TimeSpan(hours, minutes, 0);
        return offset[0] == '-' ? -span : span;
    }
}
