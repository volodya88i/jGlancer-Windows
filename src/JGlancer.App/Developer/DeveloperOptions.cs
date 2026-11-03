using System.Globalization;

namespace JGlancer.App.Developer;

/// <summary>
/// Launch arguments for benchmarks and design checks (the counterpart of the macOS -JG… hooks, D-39). Inert without
/// them; with <c>--data-dir</c> the user's own library, cache and view state are never touched.
/// <list type="bullet">
/// <item><c>--data-dir &lt;folder&gt;</c>: keep library, cache and view state there instead of %APPDATA% / %LOCALAPPDATA%.</item>
/// <item><c>--add-folder &lt;folder&gt;</c>: add a library folder at start (if not already there).</item>
/// <item><c>--launch-log &lt;file&gt;</c>: append the time from process start to the first rendered frame.</item>
/// <item><c>--snapshot &lt;png&gt;</c>: render the main window to a PNG once it has settled, then quit.</item>
/// <item><c>--benchmark &lt;file&gt;</c>: measure index, thumbnails and scrolling, write the results, then quit.</item>
/// <item><c>--theme dark|light|dark-hc|light-hc</c>, <c>--lang en|ru</c>: override the appearance and language.</item>
/// <item><c>--tile-side &lt;n&gt;</c>, <c>--select-first</c>: preset the grid for snapshots; <c>--open-first</c>: open the viewer on the first photo (<c>--open-kind video|audio</c>: of that kind), <c>--viewer-step &lt;n&gt;</c>: then page forward n times; <c>--show-filmstrip</c>: keep its filmstrip shown; <c>--segment</c>: start selecting a segment in the player.</item>
/// <item><c>--flag-check &lt;log&gt;</c>: flag, filter, toggle off, undo and search, log, quit; <c>--flag-first</c>: flag the selected file red; <c>--search &lt;text&gt;</c>: search for it.</item>
/// <item><c>--onboarding</c>: show the first-launch choice of a look (developer runs skip it otherwise).</item>
/// <item><c>--select-kind photo|video|audio</c>: with <c>--select-first</c>, the first file of that kind; <c>--quick-preview</c>: then open Quick Preview on it (a snapshot shows that window).</item>
/// <item><c>--context-menu</c>, <c>--get-info</c>: once settled, open the selection's context menu or Get Info (a snapshot then captures the screen there).</item>
/// <item><c>--batch-rename</c>: once settled, select every file and open Rename… (screen capture); <c>--file-ops-check &lt;log&gt;</c>: rename, undo, Recycle Bin, undo on the first file, log, quit.</item>
/// <item><c>--album-check &lt;log&gt;</c>: make, show, change and undo an album, log, quit; <c>--open-album</c>: show the first album.</item>
/// <item><c>--settings general|appearance|library|playback|editwith|windows|shortcuts</c>: open Settings there (rendered offscreen).</item>
/// <item><c>--window-size &lt;W&gt;x&lt;H&gt;</c>: the window's size (benchmarks at a full-screen size).</item>
/// <item><c>--no-windows-thumbcache</c>: decode every thumbnail (as on a PC whose Explorer never showed the folder).</item>
/// <item><c>--open-menu &lt;n&gt;</c>: open the n-th menu of the menu bar (0 = File) once the window shows.</item>
/// </list>
/// </summary>
internal sealed record DeveloperOptions
{
    public static DeveloperOptions Current { get; set; } = new();

    public string? DataDirectory { get; init; }
    public string? AddFolder { get; init; }
    public string? LaunchLog { get; init; }
    public string? Snapshot { get; init; }
    public string? Benchmark { get; init; }
    public string? Theme { get; init; }
    public string? Language { get; init; }
    public int? OpenMenu { get; init; }
    public double? TileSide { get; init; }
    public bool SelectFirst { get; init; }

    public bool Onboarding { get; init; }

    public string? FlagCheck { get; init; }

    /// <summary><c>--accent-check &lt;log&gt;</c>: change the accent while running, log, quit (W-93).</summary>
    public string? AccentCheck { get; init; }

    /// <summary><c>--accent &lt;#RRGGBB&gt;</c>: start with this accent (snapshots).</summary>
    public string? Accent { get; init; }

    /// <summary>`--stem-check &lt;log&gt;`: splits the first song into 4 stems next to it (the model must be in the data folder's Models), logs, quits.</summary>
    public string? StemCheck { get; init; }

    /// <summary>`--hdr-check &lt;log&gt;` (with `--open-first --open-kind video`): HDR on, then off; logs mpv's view of it, quits.</summary>
    public string? HdrCheck { get; init; }

    /// <summary>`--tracks-check &lt;log&gt;` (with `--open-first --open-kind video`): lists the tracks, mixes the audio, shows a subtitle; logs, quits.</summary>
    public string? TracksCheck { get; init; }

    /// <summary>`--update-check &lt;log&gt;`: fetches and verifies the latest release, starts its installer offscreen; quits.</summary>
    public string? UpdateCheck { get; init; }

    /// <summary>`--switch-check &lt;log&gt;` (with `--open-first --open-kind video`): times moving through the videos; quits.</summary>
    public string? SwitchCheck { get; init; }

    /// <summary>The files, folder, Quick Preview or background helper the launch asks for (Explorer, a second launch).</summary>
    public Core.Launch.LaunchArguments Launch { get; init; } = Core.Launch.LaunchArguments.None;

    /// <summary>Options followed by a value: their values are not paths to open.</summary>
    public static IReadOnlySet<string> ValueOptions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "--data-dir", "--add-folder", "--launch-log", "--snapshot", "--benchmark", "--theme", "--lang", "--open-menu",
        "--tile-side", "--flag-check", "--accent-check", "--accent", "--search", "--open-kind", "--viewer-step", "--select-kind", "--file-ops-check",
        "--album-check", "--settings", "--window-size", "--split", "--stem-check", "--hdr-check", "--switch-check", "--update-check", "--tracks-check",
    };

    public bool FlagFirst { get; init; }

    /// <summary>`--mini-player`: the selected song plays in the mini player (silent in developer runs).</summary>
    public bool MiniPlayer { get; init; }

    public string? Search { get; init; }

    /// <summary><c>--split side|stacked</c>: start in the split view (snapshots).</summary>
    public Core.Model.SplitMode? Split { get; init; }
    public bool OpenFirst { get; init; }

    /// <summary>`--save-frame` (with `--open-first --open-kind video`): saves the video's frame beside it.</summary>
    public bool SaveFrame { get; init; }

    /// <summary>`--pin-first`: pins the first subfolder to the sidebar.</summary>
    public bool PinFirst { get; init; }

    /// <summary>`--unfold-first`: unfolds the first image sequence of the grid.</summary>
    public bool UnfoldFirst { get; init; }

    /// <summary>`--compare-first`: compares the first two photos.</summary>
    public bool CompareFirst { get; init; }

    /// <summary>`--duplicates`: opens Edit ▸ Find Duplicates… (a snapshot then shows that window).</summary>
    public bool Duplicates { get; init; }

    /// <summary>`--about`: shows Help ▸ About jGlancer (a snapshot then shows that window).</summary>
    public bool About { get; init; }
    public JGlancer.Core.Model.MediaKind OpenKind { get; init; } = JGlancer.Core.Model.MediaKind.Photo;
    public int ViewerStep { get; init; }
    public bool ShowFilmstrip { get; init; }

    /// <summary><c>--music-panel</c>: with <c>--open-first --open-kind audio</c>, analyzes the song and shows the key and tempo panel in the window, so a snapshot holds it.</summary>
    public bool MusicPanel { get; init; }
    public Avalonia.Size? WindowSize { get; init; }
    public bool NoWindowsThumbnailCache { get; init; }
    public bool Segment { get; init; }
    public JGlancer.Core.Model.MediaKind? SelectKind { get; init; }
    public bool QuickPreview { get; init; }
    public bool ContextMenu { get; init; }
    public bool GetInfo { get; init; }
    public bool BatchRename { get; init; }
    public string? FileOpsCheck { get; init; }
    public string? AlbumCheck { get; init; }
    public bool OpenAlbum { get; init; }
    public string? SettingsSection { get; init; }

    /// <summary>The snapshot shows other windows over the main one: the screen there is captured.</summary>
    public bool CapturesScreen => ContextMenu || GetInfo || BatchRename;

    /// <summary>Runs that must not save view state or settings (benchmarks, snapshots).</summary>
    public bool IsEphemeral => DataDirectory is not null;

    public static DeveloperOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? Value(string name)
        {
            for (var i = 0; i + 1 < args.Count; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }
        double? Number(string name) =>
            double.TryParse(Value(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
        return new DeveloperOptions
        {
            DataDirectory = Value("--data-dir"),
            AddFolder = Value("--add-folder"),
            LaunchLog = Value("--launch-log"),
            Snapshot = Value("--snapshot"),
            Benchmark = Value("--benchmark"),
            Theme = Value("--theme"),
            Language = Value("--lang"),
            OpenMenu = (int?)Number("--open-menu"),
            TileSide = Number("--tile-side"),
            SelectFirst = args.Contains("--select-first", StringComparer.OrdinalIgnoreCase),
            Onboarding = args.Contains("--onboarding", StringComparer.OrdinalIgnoreCase),
            FlagCheck = Value("--flag-check"),
            AccentCheck = Value("--accent-check"),
            Accent = Value("--accent"),
            StemCheck = Value("--stem-check"),
            HdrCheck = Value("--hdr-check"),
            TracksCheck = Value("--tracks-check"),
            UpdateCheck = Value("--update-check"),
            SwitchCheck = Value("--switch-check"),
            Launch = Core.Launch.LaunchArguments.Parse(args, ValueOptions),
            FlagFirst = args.Contains("--flag-first", StringComparer.OrdinalIgnoreCase),
            MiniPlayer = args.Contains("--mini-player", StringComparer.OrdinalIgnoreCase),
            Search = Value("--search"),
            Split = Value("--split") switch { "side" => Core.Model.SplitMode.SideBySide, "stacked" => Core.Model.SplitMode.Stacked, _ => null },
            OpenFirst = args.Contains("--open-first", StringComparer.OrdinalIgnoreCase),
            SaveFrame = args.Contains("--save-frame", StringComparer.OrdinalIgnoreCase),
            PinFirst = args.Contains("--pin-first", StringComparer.OrdinalIgnoreCase),
            UnfoldFirst = args.Contains("--unfold-first", StringComparer.OrdinalIgnoreCase),
            CompareFirst = args.Contains("--compare-first", StringComparer.OrdinalIgnoreCase),
            Duplicates = args.Contains("--duplicates", StringComparer.OrdinalIgnoreCase),
            About = args.Contains("--about", StringComparer.OrdinalIgnoreCase),
            OpenKind = Value("--open-kind") switch { "video" => JGlancer.Core.Model.MediaKind.Video, "audio" => JGlancer.Core.Model.MediaKind.Audio, _ => JGlancer.Core.Model.MediaKind.Photo },
            ViewerStep = (int)(Number("--viewer-step") ?? 0),
            ShowFilmstrip = args.Contains("--show-filmstrip", StringComparer.OrdinalIgnoreCase),
            MusicPanel = args.Contains("--music-panel", StringComparer.OrdinalIgnoreCase),
            NoWindowsThumbnailCache = args.Contains("--no-windows-thumbcache", StringComparer.OrdinalIgnoreCase),
            Segment = args.Contains("--segment", StringComparer.OrdinalIgnoreCase),
            SelectKind = Value("--select-kind") switch { "photo" => JGlancer.Core.Model.MediaKind.Photo, "video" => JGlancer.Core.Model.MediaKind.Video, "audio" => JGlancer.Core.Model.MediaKind.Audio, _ => null },
            QuickPreview = args.Contains("--quick-preview", StringComparer.OrdinalIgnoreCase),
            ContextMenu = args.Contains("--context-menu", StringComparer.OrdinalIgnoreCase),
            GetInfo = args.Contains("--get-info", StringComparer.OrdinalIgnoreCase),
            BatchRename = args.Contains("--batch-rename", StringComparer.OrdinalIgnoreCase),
            FileOpsCheck = Value("--file-ops-check"),
            AlbumCheck = Value("--album-check"),
            OpenAlbum = args.Contains("--open-album", StringComparer.OrdinalIgnoreCase),
            SettingsSection = Value("--settings"),
            WindowSize = Value("--window-size")?.Split('x') is [var w, var h] && double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out var width) && double.TryParse(h, NumberStyles.Float, CultureInfo.InvariantCulture, out var height) ? new Avalonia.Size(width, height) : null,
        };
    }
}
