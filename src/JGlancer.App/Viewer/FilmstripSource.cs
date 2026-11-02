using JGlancer.App.Library;
using JGlancer.Core.Model;
using JGlancer.Media.Thumbnails;

namespace JGlancer.App.Viewer;

/// <summary>What the filmstrip shows for one file.</summary>
internal sealed record FilmstripItem(string Name, MediaKind Kind, ThumbnailRequest Thumbnail);

/// <summary>A viewer whose folder the filmstrip shows (macOS FilmstripSource): the photo pager or the player's queue.</summary>
internal interface IFilmstripSource
{
    int Count { get; }

    int Index { get; }

    /// <summary>Display-ready thumbnails shared with the grid.</summary>
    ThumbnailCache Bitmaps { get; }

    ThumbnailService Thumbnails { get; }

    /// <summary>The current position changed.</summary>
    event EventHandler? Changed;

    /// <summary>Items that were loading became available.</summary>
    event EventHandler? RowsLoaded;

    /// <summary>The item at <paramref name="index"/>, or null while it loads.</summary>
    FilmstripItem? Item(int index, ThumbnailBucket bucket);

    /// <summary>Tiles around <paramref name="index"/> came on screen (load what they need).</summary>
    void PrepareRows(int index);

    void MoveTo(int index);
}
