using System.Text.Json;
using System.Text.Json.Serialization;

namespace JGlancer.Core.Model;

/// <summary>
/// What the library window restores on relaunch (macOS LibraryViewState): scope, filters, sort, tile size, names,
/// selection, preview pane. Missing keys keep their defaults, so new fields never reset the rest (plain setters:
/// the JSON source generator gives missing init-only properties their type's default instead).
/// </summary>
public sealed record LibraryViewState
{
    /// <summary>Selections larger than this are not saved (Ctrl+A on a huge library).</summary>
    public const int MaxSavedSelection = 2000;

    public long? RootId { get; set; }
    public long? FolderId { get; set; }

    /// <summary>The album shown instead of a folder.</summary>
    public long? AlbumId { get; set; }

    public bool IncludeSubfolders { get; set; } = true;
    public IReadOnlyList<MediaKind> Kinds { get; set; } = [MediaKind.Video, MediaKind.Photo, MediaKind.Audio];
    public AssetSort Sort { get; set; } = AssetSort.Name;
    public bool Ascending { get; set; } = true;

    /// <summary>The top bar's star: only flagged files, or one flag; null shows every file.</summary>
    public FlagFilter? FlagFilter { get; set; }

    /// <summary>Preferred tile side in effective pixels.</summary>
    public double TileSide { get; set; } = 160;

    public bool ShowNames { get; set; } = true;
    public IReadOnlyList<long> SelectedIds { get; set; } = [];
    public long? FocusedId { get; set; }

    /// <summary>The preview pane on the right: shown, and its width.</summary>
    public bool PreviewVisible { get; set; } = true;

    /// <summary>The split view: one pane, two side by side, or two stacked (View ▸ Split View).</summary>
    public SplitMode Split { get; set; } = SplitMode.Off;

    /// <summary>The first pane's share of the split (the divider dragged).</summary>
    public double SplitRatio { get; set; } = 0.5;

    /// <summary>The orientation the split view comes back with when turned on again.</summary>
    public SplitMode LastSplit { get; set; } = SplitMode.SideBySide;

    /// <summary>Folders pinned to the top of the sidebar, in the order pinned.</summary>
    public IReadOnlyList<long> PinnedFolders { get; set; } = [];

    /// <summary>The folder sidebar's width, dragged by its right edge.</summary>
    public double SidebarWidth { get; set; } = 220;
    public double PreviewWidth { get; set; } = 360;

    /// <summary>Where Quick Preview was last (screen pixels for the position, effective pixels for the size).</summary>
    public WindowFrame? QuickPreviewFrame { get; set; }

    /// <summary>The kinds as a set; an empty saved list means all kinds.</summary>
    [JsonIgnore]
    public IReadOnlySet<MediaKind> KindSet => Kinds.Count == 0 ? AssetQuery.AllKinds : Kinds.ToHashSet();

    public string ToJson() => JsonSerializer.Serialize(this, ViewStateJson.Default.LibraryViewState);

    /// <summary>Parses saved state; anything unreadable gives the defaults.</summary>
    public static LibraryViewState FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new LibraryViewState();
        }
        try
        {
            return JsonSerializer.Deserialize(json, ViewStateJson.Default.LibraryViewState) ?? new LibraryViewState();
        }
        catch (JsonException)
        {
            return new LibraryViewState();
        }
    }
}

/// <summary>How the library shows its panes (split view).</summary>
public enum SplitMode
{
    Off,

    /// <summary>Two panes side by side, a vertical divider between them.</summary>
    SideBySide,

    /// <summary>Two panes one above the other, a horizontal divider between them.</summary>
    Stacked,
}

/// <summary>A window's remembered place: position in screen pixels, size in effective pixels.</summary>
public sealed record WindowFrame(int X, int Y, double Width, double Height);

/// <summary>Source-generated JSON (NativeAOT: no reflection).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LibraryViewState))]
internal sealed partial class ViewStateJson : JsonSerializerContext;
