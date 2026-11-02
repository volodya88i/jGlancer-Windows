using JGlancer.App.FileOps;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;

namespace JGlancer.App.Library;

/// <summary>
/// Virtual albums in the library (macOS LibraryModel+Albums, FEATURES §6): the sidebar list, the album shown in the
/// grid, and the album changes, each undoable through the app's history (<see cref="UndoableChanged"/> registers them).
/// </summary>
internal sealed partial class LibraryModel
{
    private IReadOnlyList<Album> _albums = [];
    private CancellationTokenSource? _albumsWatch;

    /// <summary>Every album in sidebar order.</summary>
    public IReadOnlyList<Album> Albums => _albums;

    /// <summary>The album shown in the grid, if any.</summary>
    public Album? CurrentAlbum => State.AlbumId is { } id ? _albums.FirstOrDefault(a => a.Id == id) : null;

    /// <summary>The album being named in the sidebar (a new one, or after Rename).</summary>
    public long? RenamingAlbumId { get; private set; }

    /// <summary>The albums changed (names, counts, covers) or the one renamed in place.</summary>
    public event EventHandler? AlbumsChanged;

    /// <summary>An undoable album change happened: the window adds it to the history.</summary>
    public event EventHandler<CustomUndo>? UndoableChanged;

    private void StartAlbums()
    {
        _albumsWatch?.Cancel();
        var stop = new CancellationTokenSource();
        _albumsWatch = stop;
        _ = WatchAlbumsAsync(stop.Token);
    }

    private async Task WatchAlbumsAsync(CancellationToken cancellation)
    {
        try
        {
            await Store.PurgeAlbumPlaceholdersAsync();
            await foreach (var albums in Store.ObserveAlbums(cancellation))
            {
                _albums = albums;
                if (State.AlbumId is { } shown && albums.All(a => a.Id != shown))
                {
                    LeaveAlbum();
                }
                AlbumsChanged?.Invoke(this, EventArgs.Empty);
                TreesChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void SelectAlbum(long albumId)
    {
        if (State.AlbumId == albumId)
        {
            return;
        }
        UpdateState(s => s.AlbumId = albumId);
        Selection.Clear();
        ReloadQuery();
        TreesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void LeaveAlbum()
    {
        if (State.AlbumId is null)
        {
            return;
        }
        UpdateState(s => s.AlbumId = null);
        Selection.Clear();
        ReloadQuery();
        TreesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void BeginAlbumRename(long? albumId)
    {
        RenamingAlbumId = albumId;
        AlbumsChanged?.Invoke(this, EventArgs.Empty);
    }

    // MARK: Changes

    /// <summary>A new album ("New Album", named in the sidebar), with <paramref name="assetIds"/> if given; returns its id.</summary>
    public async Task<long> CreateAlbumAsync(IReadOnlyList<long>? assetIds = null)
    {
        // The sidebar names a new album in place: it listens to the main model.
        if (_primary is { } primary)
        {
            return await primary.CreateAlbumAsync(assetIds);
        }
        var album = await Store.CreateAlbumAsync(L.T("New Album"));
        if (assetIds is { Count: > 0 })
        {
            await Store.AddToAlbumAsync(album.Id, assetIds);
        }
        RaiseUndoable(Deletion(album.Id, L.T("New Album")));
        BeginAlbumRename(album.Id);
        return album.Id;
    }

    public async Task RenameAlbumAsync(long albumId, string name)
    {
        RenamingAlbumId = null;
        var trimmed = name.Trim();
        var old = _albums.FirstOrDefault(a => a.Id == albumId)?.Name;
        if (trimmed.Length == 0 || old is null || old == trimmed)
        {
            AlbumsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        await Store.RenameAlbumAsync(albumId, trimmed);
        RaiseUndoable(Renaming(albumId, old));
    }

    /// <summary>Deletes the album (never its files); undo brings it back with its items, order and cover.</summary>
    public async Task DeleteAlbumAsync(long albumId)
    {
        if (_albums.FirstOrDefault(a => a.Id == albumId) is not { } album)
        {
            return;
        }
        var items = await Store.AlbumOrderAsync(albumId);
        var cover = await ChosenCoverAsync(albumId);
        await Store.DeleteAlbumAsync(albumId);
        RaiseUndoable(Recreation(album.Name, items, cover));
    }

    public async Task AddToAlbumAsync(long albumId, IReadOnlyList<long> assetIds)
    {
        var added = await Store.AddToAlbumAsync(albumId, assetIds);
        if (added.Count > 0)
        {
            RaiseUndoable(Removal(albumId, added, L.T("Add to Album")));
        }
    }

    /// <summary>Removes items from the album shown; files stay.</summary>
    public async Task RemoveFromCurrentAlbumAsync(IReadOnlyList<long> assetIds)
    {
        if (State.AlbumId is not { } albumId)
        {
            return;
        }
        var removed = await Store.RemoveFromAlbumAsync(albumId, assetIds);
        if (removed.Count > 0)
        {
            Selection.Clear();
            RaiseUndoable(Restoration(albumId, removed, L.T("Remove from Album")));
        }
    }

    public async Task SetCoverAsync(long assetId)
    {
        if (State.AlbumId is { } albumId)
        {
            await Store.SetAlbumCoverAsync(albumId, assetId);
        }
    }

    /// <summary>Moves items in front of <paramref name="before"/> (or to the end) in the album shown.</summary>
    public async Task ReorderCurrentAlbumAsync(IReadOnlyList<long> assetIds, long? before)
    {
        if (State.AlbumId is not { } albumId)
        {
            return;
        }
        var old = await Store.AlbumOrderAsync(albumId);
        await Store.MoveInAlbumAsync(albumId, assetIds, before);
        RaiseUndoable(Ordering(albumId, old));
    }

    /// <summary>The paths of an album's present items, in album order (Export to Folder…).</summary>
    public async Task<IReadOnlyList<string>> AlbumPathsAsync(long albumId)
    {
        var order = await Store.AlbumOrderAsync(albumId);
        var rows = (await Store.AssetsAsync(order)).Where(a => a.State == AssetState.Present).ToDictionary(a => a.Id, a => a.Path);
        return [.. order.Where(rows.ContainsKey).Select(id => rows[id])];
    }

    /// <summary>The selection in display order (Ctrl+N puts it into the new album, as macOS).</summary>
    public IReadOnlyList<long> SelectedInOrder() =>
        WithSequenceFrames(Selection.Selected.Select(id => (id, index: Order.IndexOf(id))).Where(p => p.index is not null).OrderBy(p => p.index).Select(p => p.id));

    /// <summary>Library asset ids of dropped files (files outside the library cannot join an album).</summary>
    public async Task<IReadOnlyList<long>> AssetIdsAsync(IEnumerable<string> paths)
    {
        var ids = new List<long>();
        foreach (var path in paths)
        {
            if ((await Store.LocationAsync(path, isDirectory: false))?.AssetId is { } id)
            {
                ids.Add(id);
            }
        }
        return ids;
    }

    private async Task<long?> ChosenCoverAsync(long albumId)
    {
        var album = (await Store.AlbumsAsync()).FirstOrDefault(a => a.Id == albumId);
        var order = await Store.AlbumOrderAsync(albumId);
        long? first = order.Count > 0 ? order[0] : null;
        return album?.CoverAssetId is { } cover && cover != first ? cover : null;
    }

    // MARK: Undo

    /// <summary>Undoes an add by removing the added items (its inverse puts them back).</summary>
    private CustomUndo Removal(long albumId, IReadOnlyList<long> ids, string name) => new(name, async () =>
    {
        var removed = await Store.RemoveFromAlbumAsync(albumId, ids);
        return Restoration(albumId, removed, name);
    });

    private CustomUndo Restoration(long albumId, IReadOnlyList<AlbumItemPosition> items, string name) => new(name, async () =>
    {
        await Store.RestoreToAlbumAsync(albumId, items);
        return Removal(albumId, [.. items.Select(i => i.AssetId)], name);
    });

    private CustomUndo Ordering(long albumId, IReadOnlyList<long> old) => new(L.T("Reorder"), async () =>
    {
        var current = await Store.AlbumOrderAsync(albumId);
        await Store.SetAlbumOrderAsync(albumId, old);
        return Ordering(albumId, current);
    });

    private CustomUndo Renaming(long albumId, string name) => new(L.T("Rename"), async () =>
    {
        var current = (await Store.AlbumsAsync()).FirstOrDefault(a => a.Id == albumId)?.Name;
        if (current is null)
        {
            return null;
        }
        await Store.RenameAlbumAsync(albumId, name);
        return Renaming(albumId, current);
    });

    /// <summary>Undoes a new album by deleting it (its inverse makes it again, with what was put in meanwhile).</summary>
    private CustomUndo Deletion(long albumId, string name) => new(name, async () =>
    {
        var album = (await Store.AlbumsAsync()).FirstOrDefault(a => a.Id == albumId);
        if (album is null)
        {
            return null;
        }
        var items = await Store.AlbumOrderAsync(albumId);
        var cover = await ChosenCoverAsync(albumId);
        await Store.DeleteAlbumAsync(albumId);
        return Recreation(album.Name, items, cover, name);
    });

    private CustomUndo Recreation(string name, IReadOnlyList<long> items, long? cover, string? title = null) => new(title ?? L.T("Delete Album"), async () =>
    {
        var album = await Store.CreateAlbumAsync(name);
        await Store.AddToAlbumAsync(album.Id, items);
        if (cover is { } chosen)
        {
            await Store.SetAlbumCoverAsync(album.Id, chosen);
        }
        return Deletion(album.Id, title ?? L.T("Delete Album"));
    });
}
