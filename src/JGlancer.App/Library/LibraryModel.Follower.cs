using JGlancer.App.FileOps;
using JGlancer.App.Services;

namespace JGlancer.App.Library;

/// <summary>
/// The second pane of the split view: a library model that follows the main one. Roots, folder trees,
/// albums, background indexing and the undo history are the main model's (shared, never loaded twice); the folder or
/// album shown, kinds, flags, search, sort, tile size, names and the selection are its own, saved apart.
/// </summary>
internal sealed partial class LibraryModel
{
    private readonly LibraryModel? _primary;

    /// <summary>A follower of <paramref name="primary"/> for the split view's second pane.</summary>
    public LibraryModel(AppServices services, ViewStateStore stateStore, LibraryModel primary)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(primary);
        Services = services;
        _stateStore = stateStore;
        _primary = primary;
        Indexer = primary.Indexer;
        State = stateStore.Load();
        _pendingRestore = State;
        Selection.Changed += (_, _) => SelectionChanged();
        WatchSequenceSetting();
    }

    /// <summary>True for the split view's second pane.</summary>
    public bool IsFollower => _primary is not null;

    /// <summary>Starts showing the folder (or album) it was left on, or the main pane's.</summary>
    public void StartFollowing()
    {
        if (_primary is not { } primary)
        {
            return;
        }
        primary.TreesChanged += OnPrimaryTreesChanged;
        primary.AlbumsChanged += OnPrimaryAlbumsChanged;
        primary.FlagsChanged += OnPrimaryFlagsChanged;
        Indexer.Changed += OnIndexerChanged;
        Mirror();
        _albums = primary._albums;
        if (State.FolderId is null && State.AlbumId is null)
        {
            UpdateState(s =>
            {
                s.FolderId = primary.State.FolderId;
                s.AlbumId = primary.State.AlbumId;
            });
        }
        ReloadQuery();
    }

    private void StopFollowing()
    {
        if (_primary is not { } primary)
        {
            return;
        }
        primary.TreesChanged -= OnPrimaryTreesChanged;
        primary.AlbumsChanged -= OnPrimaryAlbumsChanged;
        primary.FlagsChanged -= OnPrimaryFlagsChanged;
        Indexer.Changed -= OnIndexerChanged;
        _refsWatch?.Cancel();
        _totalsWork?.Cancel();
        _selectionWork?.Cancel();
    }

    private void OnPrimaryTreesChanged(object? sender, EventArgs e)
    {
        Mirror();
        // Started before the main model knew its folders: the main pane's folder, once there is one.
        if (State.FolderId is null && State.AlbumId is null && _primary!.State.FolderId is { } first && Folder(first) is not null)
        {
            SelectFolder(first);
            return;
        }
        // The folder shown went away with its root: the main pane's instead.
        if (State.FolderId is { } folder && Folder(folder) is null && _primary!.State.FolderId is { } fallback && Folder(fallback) is not null)
        {
            SelectFolder(fallback);
        }
        TreesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPrimaryAlbumsChanged(object? sender, EventArgs e)
    {
        _albums = _primary!._albums;
        AlbumsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPrimaryFlagsChanged(object? sender, EventArgs e) => FlagsChanged?.Invoke(this, EventArgs.Empty);

    private void OnIndexerChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(ActivityText));

    /// <summary>The main model's roots and trees, the same objects.</summary>
    private void Mirror()
    {
        var primary = _primary!;
        _trees.Clear();
        foreach (var (id, tree) in primary._trees)
        {
            _trees[id] = tree;
        }
        Roots = primary._roots;
    }

    /// <summary>Undoable changes go to the one history (the main model's, which the window listens to).</summary>
    private void RaiseUndoable(CustomUndo change) => (_primary ?? this).UndoableChanged?.Invoke(_primary ?? this, change);

    private void RaiseFlagUndoReplaced((CustomUndo Last, CustomUndo Change) swap) =>
        (_primary ?? this).FlagUndoReplaced?.Invoke(_primary ?? this, swap);

    /// <summary>A flag changed: every pane and star refreshes (followers hear it from the main model).</summary>
    private void RaiseFlagsChanged()
    {
        var owner = _primary ?? this;
        owner.FlagsChanged?.Invoke(owner, EventArgs.Empty);
    }
}
