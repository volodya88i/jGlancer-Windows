namespace JGlancer.Design;

/// <summary>
/// Solid icons drawn in-house for the players, the kind filters and Settings (W-99): shapes rather than thin lines, drawn on
/// a 16-pixel grid (the numbers are on the 24-unit canvas, 1.5 units = 1 pixel at the default size), so that straight
/// edges fall on whole pixels and stay sharp. Use with <c>IsFilled</c>.
/// </summary>
public static class JGSolidIcons
{
    /// <summary>Video filter: a solid frame with a play triangle cut out.</summary>
    public const string KindVideo = "F1 M4.5 4.5 L19.5 4.5 A3 3 0 0 1 22.5 7.5 L22.5 16.5 A3 3 0 0 1 19.5 19.5 L4.5 19.5 A3 3 0 0 1 1.5 16.5 L1.5 7.5 A3 3 0 0 1 4.5 4.5Z M9.75 15.75 L16.5 12 L9.75 8.25Z";

    /// <summary>Photo filter: a solid frame with a sun and mountains cut out.</summary>
    public const string KindPhoto = "F1 M4.5 3 L19.5 3 A3 3 0 0 1 22.5 6 L22.5 18 A3 3 0 0 1 19.5 21 L4.5 21 A3 3 0 0 1 1.5 18 L1.5 6 A3 3 0 0 1 4.5 3Z M5.25 9 A2.25 2.25 0 1 0 9.75 9 A2.25 2.25 0 1 0 5.25 9Z M19.5 18 L15.75 13.5 L13.5 15.75 L9.75 12 L4.5 18Z";

    /// <summary>Audio filter: two joined notes.</summary>
    public const string KindAudio = "F1 M2.25 18.75 A3.75 3.75 0 1 1 9.75 18.75 A3.75 3.75 0 1 1 2.25 18.75Z M13.5 16.5 A3.75 3.75 0 1 1 21 16.5 A3.75 3.75 0 1 1 13.5 16.5Z M7.5 4.5 L21 1.5 L21 16.5 L18.75 16.5 L18.75 6 L9.75 7.95 L9.75 18.75 L7.5 18.75Z";

    /// <summary>Repeat: two arrows chasing each other.</summary>
    public const string Loop = "F1 M3 12 L3 4.5 L16.5 4.5 L16.5 0.75 L22.5 6 L16.5 11.25 L16.5 7.5 L6 7.5 L6 12Z M21 12 L21 19.5 L7.5 19.5 L7.5 23.25 L1.5 18 L7.5 12.75 L7.5 16.5 L18 16.5 L18 12Z";

    /// <summary>Play next: a list and a play triangle.</summary>
    public const string PlayNext = "F1 M1.5 4.5 L22.5 4.5 L22.5 7.5 L1.5 7.5Z M1.5 10.5 L13.5 10.5 L13.5 13.5 L1.5 13.5Z M1.5 16.5 L13.5 16.5 L13.5 19.5 L1.5 19.5Z M16.5 10.5 L22.5 15 L16.5 19.5Z";

    /// <summary>Sound on: a speaker and two waves.</summary>
    public const string Volume = "F1 M1.5 9 L6 9 L12 3 L12 21 L6 15 L1.5 15Z M16.015 7.541 A6 6 0 0 1 16.015 16.459 L14.509 14.787 A3.75 3.75 0 0 0 14.509 9.213Z M19.026 4.197 A10.5 10.5 0 0 1 19.026 19.803 L17.52 18.131 A8.25 8.25 0 0 0 17.52 5.869Z";

    /// <summary>Sound off: a speaker and a cross.</summary>
    public const string VolumeOff = "F1 M1.5 9 L6 9 L12 3 L12 21 L6 15 L1.5 15Z M18.375 10.144 L20.644 7.875 L22.5 9.731 L20.231 12 L22.5 14.269 L20.644 16.125 L18.375 13.856 L16.106 16.125 L14.25 14.269 L16.519 12 L14.25 9.731 L16.106 7.875Z";

    /// <summary>Full screen: corners out.</summary>
    public const string FullScreen = "F1 M3 3 L10.5 3 L10.5 6 L6 6 L6 10.5 L3 10.5Z M21 10.5 L18 10.5 L18 6 L13.5 6 L13.5 3 L21 3Z M3 13.5 L6 13.5 L6 18 L10.5 18 L10.5 21 L3 21Z M21 21 L13.5 21 L13.5 18 L18 18 L18 13.5 L21 13.5Z";

    /// <summary>Leave full screen: corners in.</summary>
    public const string FullScreenExit = "F1 M7.5 3 L10.5 3 L10.5 10.5 L3 10.5 L3 7.5 L7.5 7.5Z M16.5 7.5 L21 7.5 L21 10.5 L13.5 10.5 L13.5 3 L16.5 3Z M7.5 16.5 L3 16.5 L3 13.5 L10.5 13.5 L10.5 21 L7.5 21Z M16.5 21 L13.5 21 L13.5 13.5 L21 13.5 L21 16.5 L16.5 16.5Z";

    /// <summary>Select a segment: brackets around a dash.</summary>
    public const string Segment = "F1 M3 3 L9 3 L9 6 L6 6 L6 18 L9 18 L9 21 L3 21Z M21 3 L21 21 L15 21 L15 18 L18 18 L18 6 L15 6 L15 3Z M9 10.5 L15 10.5 L15 13.5 L9 13.5Z";

    /// <summary>Fade the ends: a trapezoid.</summary>
    public const string Fades = "F1 M1.5 19.5 L7.5 6 L16.5 6 L22.5 19.5Z";

    /// <summary>Export the file or segment: an arrow rising out of a tray.</summary>
    public const string Export = "F1 M12 1.5 L18 7.5 L13.5 7.5 L13.5 15 L10.5 15 L10.5 7.5 L6 7.5Z M3 12 L6 12 L6 18 L18 18 L18 12 L21 12 L21 18 A3 3 0 0 1 18 21 L6 21 A3 3 0 0 1 3 18Z";

    /// <summary>Save the frame: a camera.</summary>
    public const string Frame = "F1 M4.5 6 L19.5 6 A3 3 0 0 1 22.5 9 L22.5 18 A3 3 0 0 1 19.5 21 L4.5 21 A3 3 0 0 1 1.5 18 L1.5 9 A3 3 0 0 1 4.5 6Z M7.5 6 L9 3 L15 3 L16.5 6Z M8.25 13.5 A3.75 3.75 0 1 0 15.75 13.5 A3.75 3.75 0 1 0 8.25 13.5Z";

    /// <summary>Subtitles: a frame with lines cut out.</summary>
    public const string Subtitles = "F1 M4.5 4.5 L19.5 4.5 A3 3 0 0 1 22.5 7.5 L22.5 16.5 A3 3 0 0 1 19.5 19.5 L4.5 19.5 A3 3 0 0 1 1.5 16.5 L1.5 7.5 A3 3 0 0 1 4.5 4.5Z M4.5 10.5 L13.5 10.5 L13.5 7.5 L4.5 7.5Z M15 10.5 L19.5 10.5 L19.5 7.5 L15 7.5Z M4.5 16.5 L9 16.5 L9 13.5 L4.5 13.5Z M10.5 16.5 L19.5 16.5 L19.5 13.5 L10.5 13.5Z";

    /// <summary>Audio tracks: level bars.</summary>
    public const string AudioTracks = "F1 M3 9 L3 9 A1.5 1.5 0 0 1 4.5 10.5 L4.5 13.5 A1.5 1.5 0 0 1 3 15 L3 15 A1.5 1.5 0 0 1 1.5 13.5 L1.5 10.5 A1.5 1.5 0 0 1 3 9Z M9 4.5 L9 4.5 A1.5 1.5 0 0 1 10.5 6 L10.5 18 A1.5 1.5 0 0 1 9 19.5 L9 19.5 A1.5 1.5 0 0 1 7.5 18 L7.5 6 A1.5 1.5 0 0 1 9 4.5Z M15 1.5 L15 1.5 A1.5 1.5 0 0 1 16.5 3 L16.5 21 A1.5 1.5 0 0 1 15 22.5 L15 22.5 A1.5 1.5 0 0 1 13.5 21 L13.5 3 A1.5 1.5 0 0 1 15 1.5Z M21 7.5 L21 7.5 A1.5 1.5 0 0 1 22.5 9 L22.5 15 A1.5 1.5 0 0 1 21 16.5 L21 16.5 A1.5 1.5 0 0 1 19.5 15 L19.5 9 A1.5 1.5 0 0 1 21 7.5Z";

    /// <summary>Settings, General: a gear.</summary>
    public const string General = "F1 M10.074 3.978 L10.552 1.222 L13.448 1.222 L13.926 3.978 L16.311 4.966 L18.598 3.355 L20.645 5.402 L19.034 7.689 L20.022 10.074 L22.778 10.552 L22.778 13.448 L20.022 13.926 L19.034 16.311 L20.645 18.598 L18.598 20.645 L16.311 19.034 L13.926 20.022 L13.448 22.778 L10.552 22.778 L10.074 20.022 L7.689 19.034 L5.402 20.645 L3.355 18.598 L4.966 16.311 L3.978 13.926 L1.222 13.448 L1.222 10.552 L3.978 10.074 L4.966 7.689 L3.355 5.402 L5.402 3.355 L7.689 4.966Z M8.625 12 A3.375 3.375 0 1 0 15.375 12 A3.375 3.375 0 1 0 8.625 12Z";

    /// <summary>Settings, Appearance: half light, half dark.</summary>
    public const string Appearance = "F1 M1.5 12 A10.5 10.5 0 1 1 22.5 12 A10.5 10.5 0 1 1 1.5 12Z M4.5 12 A7.5 7.5 0 1 0 19.5 12 A7.5 7.5 0 1 0 4.5 12Z M12 4.5 A7.5 7.5 0 0 0 12 19.5Z";

    /// <summary>Settings, Library: a folder.</summary>
    public const string Library = "F1 M3.75 3 L9.75 3 A2.25 2.25 0 0 1 12 5.25 L12 8.25 A2.25 2.25 0 0 1 9.75 10.5 L3.75 10.5 A2.25 2.25 0 0 1 1.5 8.25 L1.5 5.25 A2.25 2.25 0 0 1 3.75 3Z M4.5 6 L19.5 6 A3 3 0 0 1 22.5 9 L22.5 18 A3 3 0 0 1 19.5 21 L4.5 21 A3 3 0 0 1 1.5 18 L1.5 9 A3 3 0 0 1 4.5 6Z";

    /// <summary>Settings, Playback: a play button.</summary>
    public const string Playback = "F1 M1.5 12 A10.5 10.5 0 1 1 22.5 12 A10.5 10.5 0 1 1 1.5 12Z M9 16.875 L17.25 12 L9 7.125Z";

    /// <summary>Settings, Edit With: a pencil.</summary>
    public const string EditWith = "F1 M15.75 2.25 L21.75 8.25 L8.25 21.75 L2.25 21.75 L2.25 15.75Z";

    /// <summary>Settings, Windows: a window.</summary>
    public const string WindowsSection = "F1 M4.5 3 L19.5 3 A3 3 0 0 1 22.5 6 L22.5 18 A3 3 0 0 1 19.5 21 L4.5 21 A3 3 0 0 1 1.5 18 L1.5 6 A3 3 0 0 1 4.5 3Z M4.5 18 L19.5 18 L19.5 9 L4.5 9Z";

    /// <summary>Settings, Shortcuts: a keyboard.</summary>
    public const string Shortcuts = "F1 M4.5 4.5 L19.5 4.5 A3 3 0 0 1 22.5 7.5 L22.5 16.5 A3 3 0 0 1 19.5 19.5 L4.5 19.5 A3 3 0 0 1 1.5 16.5 L1.5 7.5 A3 3 0 0 1 4.5 4.5Z M4.5 10.5 L7.5 10.5 L7.5 7.5 L4.5 7.5Z M10.5 10.5 L13.5 10.5 L13.5 7.5 L10.5 7.5Z M16.5 10.5 L19.5 10.5 L19.5 7.5 L16.5 7.5Z M6 16.5 L18 16.5 L18 13.5 L6 13.5Z";
}
