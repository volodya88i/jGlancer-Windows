namespace JGlancer.Design;

/// <summary>
/// Icons drawn in-house where Lucide has nothing that fits (W-03), on the same 24 × 24 canvas and 2 px strokes.
/// </summary>
public static class JGCustomIcons
{
    // The players' transport glyphs: solid and sharp like the Mac's SF Symbols (play.fill, pause.fill,
    // backward.end.fill, forward.end.fill); drawn with JGIcon.IsFilled.

    /// <summary>Play: a solid triangle, a little right of center so it looks centered.</summary>
    public const string PlayFilled = "M7.5 4.5L19.5 12L7.5 19.5Z";

    /// <summary>Pause: two solid bars.</summary>
    public const string PauseFilled = "M6.5 5h3.5v14h-3.5Z M14 5h3.5v14h-3.5Z";

    /// <summary>Previous: a bar and a triangle pointing left.</summary>
    public const string PreviousFilled = "M5 5.5h2.5v13h-2.5Z M19 5.5L9.5 12L19 18.5Z";

    /// <summary>Next: a triangle pointing right and a bar.</summary>
    public const string NextFilled = "M5 5.5L14.5 12L5 18.5Z M16.5 5.5h2.5v13h-2.5Z";

    // The ones below follow Lucide's own shapes (camera, pin, columns-2, copy; ISC), drawn here because the icon
    // generator needs the Lucide package, which is not kept offline.

    /// <summary>
    /// Drag out to export (macOS MAC-029, D-123), stroked at <see cref="ExportStroke"/>: in a 15 px box (0.75…23.25 on
    /// the canvas), a 9.5 px sheet outline at the bottom left, open at its top-right corner (its top and right edges stop
    /// 4 px short), and an arrow from the sheet's middle out to the box's top-right corner with a 4 px head.
    /// </summary>
    public const string Export = "M9 9 L0.75 9 L0.75 23.25 L15 23.25 L15 15 M7.875 16.125 L23.25 0.75 M17.25 0.75 L23.25 0.75 L23.25 6.75";

    /// <summary>The export glyph's stroke: 1.5 px at the 16 px icon size.</summary>
    public const double ExportStroke = 2.25;

    /// <summary>The library as a grid of thumbnails (Lucide layout-grid), drawn filled.</summary>
    public const string ViewGrid = "M4 3h6a1 1 0 0 1 1 1v6a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1Z M14 3h6a1 1 0 0 1 1 1v6a1 1 0 0 1-1 1h-6a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1Z M14 13h6a1 1 0 0 1 1 1v6a1 1 0 0 1-1 1h-6a1 1 0 0 1-1-1v-6a1 1 0 0 1 1-1Z M4 13h6a1 1 0 0 1 1 1v6a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-6a1 1 0 0 1 1-1Z";

    /// <summary>The library as a list (bullets and lines), drawn filled.</summary>
    public const string ViewList = "M3 4.5h3v3H3Z M9 5h12v2H9Z M3 10.5h3v3H3Z M9 11h12v2H9Z M3 16.5h3v3H3Z M9 17h12v2H9Z";

    /// <summary>A file kept only in the cloud (Lucide cloud).</summary>
    public const string Cloud = "M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z";

    /// <summary>Open it to download (Lucide cloud-download).</summary>
    public const string CloudDownload = "M12 13v8 M4 14.899A7 7 0 1 1 15.71 8h1.79a4.5 4.5 0 0 1 2.5 8.242 M8 17l4 4 4-4";

    /// <summary>A problem: an exclamation mark in a circle (Lucide circle-alert).</summary>
    public const string AlertCircle = "M2 12A10 10 0 1 0 22 12A10 10 0 1 0 2 12Z M12 8v4 M12 16h.01";

    /// <summary>A pinned folder.</summary>
    public const string Pin = "M12 17v5 M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1a2 2 0 0 0 0-4H8a2 2 0 0 0 0 4a1 1 0 0 1 1 1z";

    /// <summary>Linked zoom and pan (compare): two chain links.</summary>
    public const string Link = "M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71 M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71";

    /// <summary>Swap the sides (compare): arrows both ways.</summary>
    public const string Swap = "M8 3L4 7l4 4 M4 7h16 M16 21l4-4-4-4 M20 17H4";

    /// <summary>An image sequence: stacked layers (Lucide's layers, ISC).</summary>
    public const string Stack = "M12 2L2 7l10 5 10-5-10-5z M2 17l10 5 10-5 M2 12l10 5 10-5";

}
