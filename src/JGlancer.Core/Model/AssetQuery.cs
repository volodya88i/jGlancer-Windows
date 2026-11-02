namespace JGlancer.Core.Model;

/// <summary>Which folders a query covers.</summary>
public abstract record QueryScope
{
    private QueryScope()
    {
    }

    /// <summary>Every root in the library.</summary>
    public static QueryScope Library { get; } = new LibraryScope();

    public static QueryScope Root(long id) => new RootScope(id);

    public static QueryScope InFolder(long id, bool includeSubfolders) => new FolderScope(id, includeSubfolders);

    public static QueryScope Album(long id) => new AlbumScope(id);

    public sealed record LibraryScope : QueryScope;

    /// <summary>One root folder and everything below it.</summary>
    public sealed record RootScope(long Id) : QueryScope;

    /// <summary>One folder, optionally with its subfolders.</summary>
    public sealed record FolderScope(long Id, bool IncludeSubfolders) : QueryScope;

    /// <summary>The items of a virtual album in the album's order (albums arrive in W8).</summary>
    public sealed record AlbumScope(long Id) : QueryScope;
}

/// <summary>Sort keys. Ties are broken by natural name order, then id.</summary>
public enum AssetSort
{
    /// <summary>Natural order: "IMG_2" before "IMG_10", case- and diacritic-insensitive.</summary>
    Name,
    DateModified,

    /// <summary>Capture date (EXIF / media creation date), falling back to the modification date.</summary>
    DateCaptured,
    Size,

    /// <summary>Photos, then videos, then audio.</summary>
    Kind,
}

/// <summary>Which flags a file must carry: any (<see cref="Only"/> null) or one.</summary>
public sealed record FlagFilter(AssetFlag? Only)
{
    public static FlagFilter Flagged { get; } = new((AssetFlag?)null);
}

/// <summary>What the library grid shows: a scope, a kind filter, a flag filter, search text and a sort order.</summary>
public sealed record AssetQuery
{
    public QueryScope Scope { get; init; } = QueryScope.Library;

    /// <summary>Kinds to include; an empty set matches nothing.</summary>
    public IReadOnlySet<MediaKind> Kinds { get; init; } = AllKinds;

    public AssetSort Sort { get; init; } = AssetSort.Name;

    public bool Ascending { get; init; } = true;

    /// <summary>Null shows flagged and unflagged files.</summary>
    public FlagFilter? Flags { get; init; }

    /// <summary>Text the file name must contain (case-insensitive); null or blank matches every file.</summary>
    public string? Search { get; init; }

    /// <summary>Photo, video and audio.</summary>
    public static IReadOnlySet<MediaKind> AllKinds { get; } = new HashSet<MediaKind> { MediaKind.Photo, MediaKind.Video, MediaKind.Audio };

    /// <summary>The search text trimmed; null when there is nothing to search for.</summary>
    public string? SearchText => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();

    /// <summary>The kind filter after toggling <paramref name="kind"/>; at least one kind always stays on.</summary>
    public static IReadOnlySet<MediaKind> Toggling(IReadOnlySet<MediaKind> kinds, MediaKind kind)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        var result = new HashSet<MediaKind>(kinds);
        if (!result.Add(kind) && result.Count > 1)
        {
            result.Remove(kind);
        }
        return result;
    }
}
