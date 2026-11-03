namespace JGlancer.Design;

/// <summary>Windows-only sizes that have no macOS counterpart (W-04).</summary>
public static partial class JGMetrics
{
    /// <summary>Height of the window's own title bar with the menu bar (Windows 11 caption height).</summary>
    public const double TitleBarHeight = 32;

    /// <summary>Width of the minimize / maximize / close buttons, as in native Windows title bars.</summary>
    public const double CaptionButtonWidth = 46;

    /// <summary>Size of the caption glyphs (Segoe Fluent Icons).</summary>
    public const double CaptionGlyphSize = 10;

    /// <summary>Minimum width of a drop-down menu.</summary>
    public const double MenuMinWidth = 200;

    /// <summary>Background of the close button under the pointer: the Windows system red, not a design token.</summary>
    public const uint CaptionCloseHover = 0xFFC42B1C;

    /// <summary>Thumbnail size range (macOS JGMetrics.tileSideRange, not in the exported tokens).</summary>
    public const double TileSideMin = 80;

    public const double TileSideMax = 320;

    /// <summary>Corner radius of the duration badge on a tile (half of the small radius, as on macOS).</summary>
    public const double BadgeRadius = 3;

    /// <summary>
    /// Seconds a tile's selection and hover take to fade in or out: the implicit Core Animation duration of the macOS
    /// tile layers' border and background colors.
    /// </summary>
    public const double TileFadeDuration = 0.25;

    /// <summary>Opacity of the light color on the part of an audio waveform not played yet (W-71).</summary>
    public const double WaveformUnplayedOpacity = 0.35;

    /// <summary>Seconds one column of an audio waveform takes to flow into its new shape (W-82)…</summary>
    public const double WaveformRevealDuration = 0.45;

    /// <summary>… and how much later the last column starts than the first, as a share of that.</summary>
    public const double WaveformRevealSpread = 0.6;

    /// <summary>Degrees the cards under a folded image sequence are turned (W-81).</summary>
    public const double SequenceCardAngle = 3;

    /// <summary>How far each card shows beyond the one in front, as a share of the tile's side.</summary>
    public const double SequenceCardOffset = 0.035;

    /// <summary>Seconds between one frame and the next leaving or entering a sequence's stack.</summary>
    public const double SequenceFrameStagger = 0.012;

    /// <summary>The latest a frame starts moving (long sequences).</summary>
    public const double SequenceStaggerLimit = 0.25;

    /// <summary>Frames a second a folded sequence plays at in the previews (W-86).</summary>
    public const double SequencePreviewRate = 24;

    /// <summary>Seconds the grid's tiles take to glide to their new places and sizes (resize, names, sort; W-69).</summary>
    public const double TileReflowDuration = 0.3;

    /// <summary>
    /// Line heights of the audio stage's title (heading) and "artist · album" (body): fixed, so the text box under the
    /// cover always has the same height (two title lines and one subtitle line) and the cover never moves.
    /// </summary>
    public const double StageTitleLineHeight = 24;

    public const double StageSubtitleLineHeight = 18;

    /// <summary>The audio stage's text box: two title lines, the gap, the subtitle line.</summary>
    public const double StageTextHeight = 2 * StageTitleLineHeight + JGSpacing.Xxs + StageSubtitleLineHeight;

    /// <summary>Seconds the audio player's blurred backdrop takes to flow from one song's cover to the next.</summary>
    public const double BackdropCrossfadeDuration = 0.8;

    /// <summary>How much of the overlay scrim the viewers' filmstrip keeps (lighter than the top bar and controls).</summary>
    public const double FilmstripScrimOpacity = 0.5;

    /// <summary>The folder sidebar's width range when dragged by its edge (default <see cref="SidebarWidth"/>).</summary>
    public const double SidebarMinWidth = 180;

    public const double SidebarMaxWidth = 440;

    /// <summary>A light frost behind the viewers' filmstrip and bottom controls (standard deviation, effective pixels).</summary>
    public const double BackdropBlurRadius = 6;

    /// <summary>How far (effective pixels) a press moves before it becomes a drag.</summary>
    public const double DragThreshold = 4;

    /// <summary>Seconds of pause in typing before a search runs (macOS JGMetrics.searchDelayMilliseconds).</summary>
    public const double SearchDelay = 0.15;

    /// <summary>Seconds after the flag key in which a digit picks the color (macOS FlagChord, W-05).</summary>
    public const double FlagChordWindow = 0.7;

    /// <summary>Small inline glyphs: breadcrumb separators, dismiss buttons (macOS uses 10–12 pt symbols there).</summary>
    public const double IconSizeSmall = 12;

    /// <summary>The audio glyph on a song without cover art (macOS: twice the tile glyph).</summary>
    public const double CoverGlyphSize = TileGlyphSize * 2;

/// <summary>Segment brackets on the scrubber (macOS JGMetrics, D-73): the horizontal ends of [ ] …</summary>
    public const double SegmentBracketSerif = 6;

    /// <summary>… the accent tint between them …</summary>
    public const double SegmentTintOpacity = 0.16;

    /// <summary>… how near an end a press has to be to drag it …</summary>
    public const double SegmentEdgeReach = 6;

    /// <summary>… and how much the timeline outside the segment is dimmed (bg.media over it).</summary>
    public const double SegmentOutsideOpacity = 0.65;

    /// <summary>Zooming a scrubber: points per wheel notch, and zoom per point (e^(points × this)).</summary>
    public const double WheelLinePoints = 10;

    public const double ZoomPerPoint = 0.01;

    /// <summary>The plain slider (W-98): thickness of its track.</summary>
    public const double SliderThickness = 4;

    /// <summary>Width of confirmation dialogs.</summary>
    public const double DialogWidth = 420;

    /// <summary>The audio player's key and tempo panel (W-107).</summary>
    public const double MusicPanelWidth = 340;

    /// <summary>The fields for a typed target pitch and tempo.</summary>
    public const double MusicShiftFieldWidth = 56;

    /// <summary>
    /// The bottom rows of the library window, the sidebar's Trash and the status bar, one height so their top lines meet
    /// (W-110; the status bar was 24 and the Trash row 37).
    /// </summary>
    public const double FooterHeight = 36;

    /// <summary>How much of the popup color tints frosted menus and panels (W-111): enough to read text over any backdrop.</summary>
    public const double PopupGlassTintOpacity = 0.8;

    /// <summary>How much of their own color tints frosted panels inside a window (toasts, progress).</summary>
    public const double WindowGlassTintOpacity = 0.62;

    /// <summary>The blur of frosted menus and panels, in effective pixels (standard deviation).</summary>
    public const double PopupGlassBlur = 32;

    /// <summary>The blur of frosted panels inside a window, in effective pixels (standard deviation).</summary>
    public const double WindowGlassBlur = 18;

    /// <summary>The chosen row of a menu over glass: the accent, this much of it, so the glass stays visible (W-111).</summary>
    public const double AccentWashOpacity = 0.28;

    /// <summary>Tooltips wrap at this width.</summary>
    public const double ToolTipMaxWidth = 320;

    /// <summary>The key and tempo panel's − and + buttons; Reset below them is as wide as both ("Сбросить" fits).</summary>
    public const double MusicStepWidth = 40;
}
