using System.Text.Json;
using System.Text.Json.Serialization;

namespace JGlancer.Core.Settings;

public enum AppLanguage
{
    System,
    English,
    Russian,
}

public enum AppTheme
{
    System,
    Dark,
    Light,

    /// <summary>Dark with black surfaces (W-97).</summary>
    Black,
}

/// <summary>How the viewers' filmstrip comes and goes.</summary>
public enum FilmstripReveal
{
    /// <summary>A chevron above the strip hides and shows it by hand.</summary>
    Button,

    /// <summary>The strip appears while the pointer is near the bottom and hides shortly after it leaves.</summary>
    Automatic,
}

/// <summary>The audio scrubber (W-100): the waveform or the plain timeline of video.</summary>
public enum WaveformStyle
{
    /// <summary>Only in settings saved before W-100, read as <see cref="Narrow"/>.</summary>
    Full,

    /// <summary>The waveform.</summary>
    Narrow,
    Timeline,
}

/// <summary>How the media-kind filters of the top bar look.</summary>
public enum KindFilterStyle
{
    /// <summary>"VIDEO", "PHOTO", "AUDIO" chips.</summary>
    Labels,

    /// <summary>Small icons joined in one group, split by hairlines.</summary>
    Icons,
}

/// <summary>
/// The app's preferences (macOS AppSettings, FEATURES §14), saved as one value. Keys missing from older files keep
/// their defaults, so new settings never reset the others.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Choices offered for the thumbnail cache cap, in gigabytes.</summary>
    public static IReadOnlyList<int> CacheChoices { get; } = [1, 2, 4, 8];

    /// <summary>False until the first-launch choice of a look was answered or skipped.</summary>
    public bool HasChosenAppearance { get; set; }

    public AppLanguage Language { get; set; } = AppLanguage.System;

    /// <summary>Look for a newer version on GitHub at most once a day (W-101).</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>When the app last got an answer about new versions.</summary>
    public DateTimeOffset? LastUpdateCheck { get; set; }

    /// <summary>The version the user chose to skip; a later one is offered again.</summary>
    public string? SkippedUpdateVersion { get; set; }

    /// <summary>Dark by default (macOS D-20).</summary>
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>The accent the user chose as <c>#RRGGBB</c>; null: the app's own (W-93).</summary>
    public string? AccentColor { get; set; }

    /// <summary>Cap of the thumbnail cache on disk, in gigabytes.</summary>
    public int ThumbnailCacheGigabytes { get; set; } = 2;

    /// <summary>The preview pane shows camera, codec and other details (its ⓘ, Settings → Library → Preview).</summary>
    public bool PreviewDetails { get; set; }

    /// <summary>Space in Explorer opens jGlancer's Quick Preview (a helper starts with Windows; opt-in).</summary>
    public bool ExplorerSpacePreview { get; set; }

    /// <summary>While that preview is open, clicking other files in Explorer shows them.</summary>
    public bool ExplorerPreviewFollowsClicks { get; set; } = true;

    /// <summary>Selecting a segment, the wheel alone zooms the timeline (otherwise Ctrl+wheel).</summary>
    public bool TimelineWheelZoom { get; set; }

    /// <summary>Image sequences (frame_001 … frame_090) fold into one stack in the grid (W-81).</summary>
    public bool GroupSequences { get; set; } = true;

    /// <summary>The split window's mode (4 or 6 stems), remembered (W-85).</summary>
    public Stems.StemMode StemMode { get; set; } = Stems.StemMode.Four;

    /// <summary>Split with the full-precision model (twice the download) instead of the compact one (W-92).</summary>
    public bool StemModelFull { get; set; }

    /// <summary>The stems ticked in the split window; null: all.</summary>
    public IReadOnlyList<Stems.StemKind>? StemTicks { get; set; }

    /// <summary>The video player shows HDR through mpv's own window (W-87).</summary>
    public bool HdrVideo { get; set; }

    /// <summary>Quick Preview starts video and audio by itself.</summary>
    public bool AutoplayInPreview { get; set; } = true;

    /// <summary>Decor: "+" marks at the corners of selected tiles.</summary>
    public bool CrosshairMarks { get; set; }

    /// <summary>The audio scrubber's look: the waveform, a narrow one, or the plain timeline (W-71).</summary>
    public WaveformStyle Waveform { get; set; } = WaveformStyle.Narrow;

    /// <summary>The video scrubber's look: the plain timeline, or the waveform of its sound as in the audio player.</summary>
    public WaveformStyle VideoWaveform { get; set; } = WaveformStyle.Timeline;

    /// <summary>Fades and slides off even when Windows' animation effects are on.</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>A heavily blurred cover art behind audio files (defaults = the Performance preset).</summary>
    public bool CoverBackdrop { get; set; }

    /// <summary>The library's bottom line with the selection's count and size.</summary>
    public bool ShowStatusBar { get; set; } = true;

    /// <summary>Menus, panels and toasts as frosted glass (W-111); on unless turned off, in both presets.</summary>
    public bool FrostedGlass { get; set; } = true;

    /// <summary>The audio player's blurred backdrop flows from one song's into the next one's.</summary>
    public bool BackdropCrossfade { get; set; } = true;

    public FilmstripReveal FilmstripReveal { get; set; } = FilmstripReveal.Button;

    /// <summary>The filmstrip hidden by its chevron (Button mode), remembered.</summary>
    public bool FilmstripCollapsed { get; set; }

    /// <summary>Swiping between photos drags the neighbor in from the edge.</summary>
    public bool SwipeAnimation { get; set; }

    /// <summary>The grid's tiles glide to their new places and sizes (tile size, names, sort; W-69).</summary>
    public bool GridAnimation { get; set; }

    public KindFilterStyle KindFilterStyle { get; set; } = KindFilterStyle.Labels;

    public string ToJson() => JsonSerializer.Serialize(this, AppSettingsJson.Default.AppSettings);

    /// <summary>Reads saved settings; anything unreadable gives the defaults, out-of-range values are brought back.</summary>
    public static AppSettings FromJson(string? json)
    {
        AppSettings? read = null;
        try
        {
            read = json is null ? null : JsonSerializer.Deserialize(json, AppSettingsJson.Default.AppSettings);
        }
        catch (JsonException)
        {
        }
        var settings = read ?? new AppSettings();
        if (!CacheChoices.Contains(settings.ThumbnailCacheGigabytes))
        {
            settings.ThumbnailCacheGigabytes = 2;
        }
        if (settings.Waveform == WaveformStyle.Full)
        {
            settings.Waveform = WaveformStyle.Narrow;
        }
        settings.AccentColor = AccentColorText.TryParse(settings.AccentColor, out var accent) ? AccentColorText.Format(accent) : null;
        return settings;
    }
}

/// <summary>
/// The two looks offered on first launch and in Settings → Appearance (macOS AppearancePreset, FEATURES §12): lighter
/// or richer. A preset sets only its own options; changing one of them by hand leaves "your own mix".
/// </summary>
public enum AppearancePreset
{
    Performance,
    Beauty,
}

public static class AppearancePresets
{
    /// <summary><paramref name="settings"/> with the preset's options.</summary>
    public static AppSettings Applied(this AppearancePreset preset, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var beauty = preset == AppearancePreset.Beauty;
        return settings with
        {
            CoverBackdrop = beauty,
            FilmstripReveal = beauty ? FilmstripReveal.Automatic : FilmstripReveal.Button,
            SwipeAnimation = beauty,
            GridAnimation = beauty,
        };
    }

    /// <summary>The preset whose options <paramref name="settings"/> match, or null when they were mixed by hand.</summary>
    public static AppearancePreset? Matching(AppSettings settings) =>
        Enum.GetValues<AppearancePreset>().Cast<AppearancePreset?>().FirstOrDefault(p => p!.Value.Applied(settings) == settings);
}

/// <summary>Source-generated JSON (NativeAOT: no reflection).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJson : JsonSerializerContext;
