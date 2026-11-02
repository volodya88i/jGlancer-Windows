using System.Globalization;
using JGlancer.Core.Localization;

namespace JGlancer.Core.Model;

/// <summary>The preview pane's lines about a file in mono (macOS PreviewFacts, DESIGN.md §8).</summary>
public static class PreviewFacts
{
    /// <summary>Duration, dimensions and size: <c>1:02 · 1920 × 1080 · 12.4 MB</c>.</summary>
    public static string Line(Asset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var parts = new List<string>();
        if (asset.Duration is { } duration && DurationFormat.Positional(duration) is { } text)
        {
            parts.Add(text);
        }
        if (asset.Width is { } width && asset.Height is { } height)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{width} × {height}"));
        }
        parts.Add(ByteFormat.Format(asset.Size));
        return string.Join(" · ", parts);
    }

    /// <summary>When it was taken, else last modified: the short date and time of the user's culture, local time.</summary>
    public static string Date(Asset asset, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(asset);
        culture ??= CultureInfo.CurrentCulture;
        var date = (asset.CapturedAt ?? asset.ModifiedAt).ToLocalTime();
        return date.ToString("d", culture) + " " + date.ToString("t", culture);
    }
}
