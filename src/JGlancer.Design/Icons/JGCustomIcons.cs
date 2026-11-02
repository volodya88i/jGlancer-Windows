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

    /// <summary>A pinned folder.</summary>
    public const string Pin = "M12 17v5 M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1a2 2 0 0 0 0-4H8a2 2 0 0 0 0 4a1 1 0 0 1 1 1z";

    /// <summary>Linked zoom and pan (compare): two chain links.</summary>
    public const string Link = "M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71 M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71";

    /// <summary>Swap the sides (compare): arrows both ways.</summary>
    public const string Swap = "M8 3L4 7l4 4 M4 7h16 M16 21l4-4-4-4 M20 17H4";

    /// <summary>An image sequence: stacked layers (Lucide's layers, ISC).</summary>
    public const string Stack = "M12 2L2 7l10 5 10-5-10-5z M2 17l10 5 10-5 M2 12l10 5 10-5";

}
