using CommunityToolkit.Mvvm.ComponentModel;
using JGlancer.App.Services;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Core.Scanning;
using JGlancer.Core.Store;
using JGlancer.Media.Thumbnails;

namespace JGlancer.App.Library;

/// <summary>A problem to show in the status bar (non-modal).</summary>
internal enum LibraryProblem
{
    None,
    AlreadyInLibrary,
    OverlapsLibraryFolder,
    FolderUnavailable,
}

/// <summary>Keys the grid offers to Quick Preview first.</summary>
internal enum QuickPreviewKey
{
    /// <summary>Space: opens or closes it (leaves its full screen first).</summary>
    Space,

    /// <summary>Esc: closes it (leaves its full screen first) when it is open.</summary>
    Escape,

    /// <summary>Enter: play / pause when it is open (the viewer opens otherwise).</summary>
    Enter,

    /// <summary>F11: its full screen when it is open.</summary>
    FullScreen,
}

internal sealed class QuickPreviewKeyEventArgs(QuickPreviewKey key) : EventArgs
{
    public QuickPreviewKey Key { get; } = key;

    public bool Handled { get; set; }
}

/// <summary>
/// State and intents of the library window (macOS LibraryModel): roots and folders, the current query and its sorted
/// index, the selection, totals, background indexing. Lives on the UI thread; views bind to it, the grid drives it.
/// </summary>
internal sealed partial class LibraryModel : ObservableObject
{
    private readonly ViewStateStore _stateStore;
    private readonly Dictionary<long, CancellationTokenSource> _folderWatches = [];
    // Folders added in this session wait for their first scan before the sidebar shows them (W-70): a drive's
    // thousands of folders would rebuild the tree after every one. Then they open.
    private readonly HashSet<long> _waitingRoots = [];
    private readonly HashSet<long> _openWhenLoaded = [];
    private bool _rootsSeen;
    private readonly Dictionary<long, FolderNode> _trees = [];
    private CancellationTokenSource? _refsWatch;
    private CancellationTokenSource? _totalsWork;
    private CancellationTokenSource? _selectionWork;
    private LibraryViewState? _pendingRestore;
    private IReadOnlyList<RootFolder> _roots = [];
    private long _totalBytes;
    private long _selectedBytes;
    private LibraryProblem _problem;
    private bool _isLoaded;

    public LibraryModel(AppServices services, ViewStateStore stateStore)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
        _stateStore = stateStore;
        Indexer = new LibraryIndexer(services.Scanner);
        Indexer.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(ActivityText));
            TreesChanged?.Invoke(this, EventArgs.Empty);
        };
        State = stateStore.Load();
        _pendingRestore = State;
        Selection.Changed += (_, _) => SelectionChanged();
        WatchSequenceSetting();
    }

    /// <summary>Settings → Library → Group image sequences applies at once.</summary>
    private void WatchSequenceSetting() => Settings.SettingsStore.Current.Changed += (_, change) =>
    {
        if (change.Old.GroupSequences != change.New.GroupSequences && _refsWatch is not null)
        {
            ReloadQuery();
        }
    };

    public AppServices Services { get; }
    public LibraryStore Store => Services.Store;
    public ThumbnailService Thumbnails => Services.Thumbnails;
    public LibraryIndexer Indexer { get; }
    public SelectionModel Selection { get; } = new();

    /// <summary>Restorable view state: scope, filters, sort, tile size, names, saved selection.</summary>
    public LibraryViewState State { get; private set; }

    public IReadOnlyList<RootFolder> Roots
    {
        get => _roots;
        private set
        {
            if (SetProperty(ref _roots, value))
            {
                OnPropertyChanged(nameof(HasRoots));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public bool HasRoots => _roots.Count > 0;

    public IReadOnlyDictionary<long, FolderNode> Trees => _trees;

    /// <summary>Folders expanded in the sidebar.</summary>
    public HashSet<long> ExpandedFolders { get; } = [];

    /// <summary>The sorted index of the current query and its id → position map.</summary>
    public IReadOnlyList<AssetRef> Refs { get; private set; } = [];
    public OrderedIds Order { get; private set; } = OrderedIds.Empty;

    /// <summary>True once the first index of the current query has arrived.</summary>
    public bool IsLoaded
    {
        get => _isLoaded;
        private set
        {
            if (SetProperty(ref _isLoaded, value))
            {
                OnPropertyChanged(nameof(IsEmptyFolder));
            }
        }
    }

    public bool IsEmptyFolder => HasRoots && IsLoaded && Refs.Count == 0;

    public LibraryProblem Problem
    {
        get => _problem;
        set
        {
            if (SetProperty(ref _problem, value))
            {
                OnPropertyChanged(nameof(ProblemText));
            }
        }
    }

    /// <summary>Raised when <see cref="Refs"/> changed (grid reload).</summary>
    public event EventHandler? RefsChanged;

    /// <summary>Raised when roots, folder trees, expansion, offline state or the selected folder changed (sidebar).</summary>
    public event EventHandler? TreesChanged;

    /// <summary>Raised when the view state changed (top bar, grid layout).</summary>
    public event EventHandler? StateChanged;

    /// <summary>Asks the viewer to open an asset (W3).</summary>
    public event EventHandler<long>? OpenRequested;

    /// <summary>Asks the window's mini player to play a song (Alt+double-click, W-68).</summary>
    public event EventHandler<long>? MiniPlayerRequested;

    /// <summary>A key for Quick Preview from the grid (macOS onQuickPreviewKey); the window owns Quick Preview.</summary>
    public event EventHandler<QuickPreviewKeyEventArgs>? QuickPreviewKey;

    /// <summary>Asks the view to edit the name of <paramref name="assetId"/> on its tile.</summary>
    public event EventHandler<long>? RenameRequested;

    public void RequestRename(long assetId) => RenameRequested?.Invoke(this, assetId);

    /// <summary>Offers <paramref name="key"/> to Quick Preview; true when it took it.</summary>
    public bool OfferQuickPreviewKey(QuickPreviewKey key)
    {
        var args = new QuickPreviewKeyEventArgs(key);
        QuickPreviewKey?.Invoke(this, args);
        return args.Handled;
    }

    /// <summary>Total size of the selected files (status bar, preview pane).</summary>
    public long SelectedBytes => _selectedBytes;

    // MARK: Preview pane

    public void SetPreviewVisible(bool visible) => UpdateState(s => s.PreviewVisible = visible);

    /// <summary>Within <see cref="JGlancer.Design.JGMetrics.PreviewPaneMinWidth"/> … <see cref="JGlancer.Design.JGMetrics.PreviewPaneMaxWidth"/>.</summary>
    public void SetSidebarWidth(double width)
    {
        var clamped = Math.Clamp(width, JGlancer.Design.JGMetrics.SidebarMinWidth, JGlancer.Design.JGMetrics.SidebarMaxWidth);
        if (clamped != State.SidebarWidth)
        {
            UpdateState(s => s.SidebarWidth = clamped);
        }
    }

    public void SetPreviewWidth(double width)
    {
        var clamped = Math.Clamp(width, JGlancer.Design.JGMetrics.PreviewPaneMinWidth, JGlancer.Design.JGMetrics.PreviewPaneMaxWidth);
        if (clamped != State.PreviewWidth)
        {
            UpdateState(s => s.PreviewWidth = clamped);
        }
    }

    // MARK: Status bar

    public string StatusText
    {
        get
        {
            if (!HasRoots || !IsLoaded)
            {
                return "";
            }
            return Selection.IsEmpty
                ? L.Plural("{0} items", Refs.Count) + "  ·  " + ByteFormat.Format(_totalBytes)
                : L.T("{0} of {1} selected", Selection.Count, Refs.Count) + "  ·  " + ByteFormat.Format(_selectedBytes);
        }
    }

    public string ActivityText => Indexer.Activity switch
    {
        LibraryActivity.Indexing => L.T("Indexing…"),
        LibraryActivity.ReadingDetails => L.T("Reading details…"),
        _ => "",
    };

    public string ProblemText => Problem switch
    {
        LibraryProblem.AlreadyInLibrary => L.T("This folder is already in the library."),
        LibraryProblem.OverlapsLibraryFolder => L.T("This folder is inside a library folder or contains one."),
        LibraryProblem.FolderUnavailable => L.T("The folder is not available."),
        _ => "",
    };

    // MARK: Startup

    public async Task StartAsync()
    {
        Indexer.FirstScanEnded += (_, rootId) => ShowScannedRoot(rootId);
        _ = WatchRootsAsync();
        StartAlbums();
        ReloadQuery();
        await Task.CompletedTask.ConfigureAwait(true);
    }

    private async Task WatchRootsAsync()
    {
        await foreach (var roots in Store.ObserveRoots())
        {
            RootsChanged(roots);
        }
    }

    private void RootsChanged(IReadOnlyList<RootFolder> roots)
    {
        var ids = roots.Select(r => r.Id).ToHashSet();
        foreach (var (id, stop) in _folderWatches.Where(pair => !ids.Contains(pair.Key)).ToList())
        {
            stop.Cancel();
            _folderWatches.Remove(id);
            _waitingRoots.Remove(id);
            Indexer.StopWatching(id);
            _trees.Remove(id);
        }
        foreach (var root in roots.Where(r => !_folderWatches.ContainsKey(r.Id)))
        {
            var stop = new CancellationTokenSource();
            _folderWatches[root.Id] = stop;
            // The roots of the first report were there before; later ones were just added.
            if (_rootsSeen)
            {
                _waitingRoots.Add(root.Id);
                Indexer.StartWatching(root, isNew: true);
            }
            else
            {
                _ = WatchFoldersAsync(root.Id, stop.Token);
                Indexer.StartWatching(root);
            }
        }
        _rootsSeen = true;
        Roots = roots;
        if (roots.Count == 0 && State.FolderId is not null)
        {
            UpdateState(s => s.FolderId = null);
            ReloadQuery();
        }
        else if (State.FolderId is { } folderId && _trees.Count == roots.Count && Folder(folderId) is null && roots.Count > 0)
        {
            // The shown folder vanished with its root: fall back to the first root.
            if (_trees.TryGetValue(roots[0].Id, out var first))
            {
                SelectFolder(first.Id);
            }
        }
        TreesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A folder's first scan ended: it shows in the sidebar now, and opens.</summary>
    private void ShowScannedRoot(long rootId)
    {
        if (_waitingRoots.Remove(rootId) && _folderWatches.TryGetValue(rootId, out var stop))
        {
            _openWhenLoaded.Add(rootId);
            _ = WatchFoldersAsync(rootId, stop.Token);
        }
    }

    private async Task WatchFoldersAsync(long rootId, CancellationToken cancellation)
    {
        try
        {
            await foreach (var folders in Store.ObserveFolders(rootId, cancellation))
            {
                if (!_trees.ContainsKey(rootId) && folders.FirstOrDefault(f => f.ParentId is null) is { } top)
                {
                    ExpandedFolders.Add(top.Id);
                }
                if (FolderNode.Tree(folders) is { } tree)
                {
                    var before = _trees.GetValueOrDefault(rootId);
                    _trees[rootId] = tree;
                    if (State.FolderId is null || _openWhenLoaded.Remove(rootId))
                    {
                        SelectFolder(tree.Id);
                    }
                    else if (State.FolderId is { } shown && before?.Find(shown) is not null && tree.Find(shown) is null)
                    {
                        // The folder shown lost its last media file and left the sidebar: its root instead.
                        SelectFolder(tree.Id);
                    }
                }
                TreesChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // MARK: Roots

    /// <summary>
    /// Adds folders chosen in a picker or dropped on the window; never touches files. <paramref name="quietIfPresent"/>
    /// skips the "already in the library" problem (the developer <c>--add-folder</c> on a reused library).
    /// </summary>
    public async Task AddFoldersAsync(IEnumerable<string> paths, bool quietIfPresent = false)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (_primary is { } primary)
        {
            await primary.AddFoldersAsync(paths, quietIfPresent);
            return;
        }
        foreach (var path in paths)
        {
            try
            {
                // A whole drive takes long to scan and brings everything on it: the user confirms first.
                if (IsDriveRoot(path) && ConfirmWholeDrive is { } confirm && !await confirm(path))
                {
                    continue;
                }
                // It shows in the sidebar, and opens, once its first scan ended (a progress card meanwhile).
                await Services.Scanner.RegisterRootAsync(path);
            }
            catch (RootAlreadyExistsException)
            {
                if (!quietIfPresent)
                {
                    Problem = LibraryProblem.AlreadyInLibrary;
                }
            }
            catch (RootOverlapsException)
            {
                Problem = LibraryProblem.OverlapsLibraryFolder;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Problem = LibraryProblem.FolderUnavailable;
            }
        }
    }

    /// <summary>Asks whether to add a whole drive (the window's dialog); null adds without asking.</summary>
    public Func<string, Task<bool>>? ConfirmWholeDrive { get; set; }

    private static bool IsDriveRoot(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return Path.GetPathRoot(full) is { } root && string.Equals(full.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>Removes a root from the library. Files on disk are never touched.</summary>
    public async Task RemoveRootAsync(long rootId)
    {
        Indexer.StopWatching(rootId);
        await Store.RemoveRootAsync(rootId);
    }

    // MARK: Query

    /// <summary>What the grid shows: the folder or album (the whole library while searching Everywhere), the kind and flag filters, the search.</summary>
    public AssetQuery Query => new()
    {
        Scope = IsSearching && SearchEverywhere ? QueryScope.Library
            : State.AlbumId is { } album ? QueryScope.Album(album)
            : State.FolderId is { } folder ? QueryScope.InFolder(folder, State.IncludeSubfolders)
            : QueryScope.Library,
        Kinds = State.KindSet,
        Sort = State.Sort,
        Ascending = State.Ascending,
        Flags = State.FlagFilter,
        Search = IsSearching ? SearchText : null,
    };

    public void UpdateState(Action<LibraryViewState> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var next = State with { };
        change(next);
        State = next;
        _stateStore.Save(State);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReloadQuery()
    {
        _refsWatch?.Cancel();
        var stop = new CancellationTokenSource();
        _refsWatch = stop;
        IsLoaded = false;
        _ = WatchRefsAsync(Query, stop.Token);
    }

    private async Task WatchRefsAsync(AssetQuery query, CancellationToken cancellation)
    {
        try
        {
            await foreach (var refs in Store.ObserveAssetRefs(query, cancellation))
            {
                // The sequences come with the files, so a stack never shows unfolded first (W-81).
                var sequences = GroupsSequences ? await Store.SequencesAsync(query) : [];
                if (!cancellation.IsCancellationRequested)
                {
                    SequencesArrived(refs, sequences);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RefsArrived(IReadOnlyList<AssetRef> refs)
    {
        Refs = refs;
        Order = new OrderedIds(refs.Select(r => r.Id).ToList());
        if (_pendingRestore is { } restore)
        {
            _pendingRestore = null;
            Selection.Restore(restore.SelectedIds.Where(Order.Contains), restore.FocusedId);
        }
        Selection.Retain(Order);
        IsLoaded = true;
        OnPropertyChanged(nameof(IsEmptyFolder));
        RefsChanged?.Invoke(this, EventArgs.Empty);
        RefreshTotals();
    }

    private void RefreshTotals()
    {
        _totalsWork?.Cancel();
        var stop = new CancellationTokenSource();
        _totalsWork = stop;
        _ = Load();
        async Task Load()
        {
            var totals = await Store.TotalsAsync(Query);
            if (!stop.IsCancellationRequested)
            {
                _totalBytes = totals.Bytes;
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    private void SelectionChanged()
    {
        UpdateState(s =>
        {
            s.SelectedIds = Selection.Count <= LibraryViewState.MaxSavedSelection ? [.. Selection.Selected] : [];
            s.FocusedId = Selection.Focus;
        });
        _selectionWork?.Cancel();
        var ids = Selection.Selected.ToList();
        if (ids.Count == 0)
        {
            _selectedBytes = 0;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(SelectedBytes));
            return;
        }
        var stop = new CancellationTokenSource();
        _selectionWork = stop;
        _ = Load();
        async Task Load()
        {
            var bytes = await Store.TotalSizeAsync(ids);
            if (!stop.IsCancellationRequested)
            {
                _selectedBytes = bytes;
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(SelectedBytes));
            }
        }
    }

    /// <summary>Done when the next list of files has arrived and the views have it (or after a few seconds).</summary>
    public async Task NextRefsAsync()
    {
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Done(object? sender, EventArgs e) => arrived.TrySetResult();
        RefsChanged += Done;
        await Task.WhenAny(arrived.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        RefsChanged -= Done;
    }

    public void SelectFolder(long folderId)
    {
        if (State.FolderId == folderId && State.AlbumId is null)
        {
            return;
        }
        UpdateState(s =>
        {
            s.FolderId = folderId;
            s.AlbumId = null;
        });
        Selection.Clear();
        ReloadQuery();
        TreesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleKind(MediaKind kind)
    {
        var kinds = AssetQuery.Toggling(State.KindSet, kind);
        if (kinds.SetEquals(State.KindSet))
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        UpdateState(s => s.Kinds = [.. new[] { MediaKind.Video, MediaKind.Photo, MediaKind.Audio }.Where(kinds.Contains)]);
        ReloadQuery();
    }

    public void SetSort(AssetSort sort)
    {
        UpdateState(s => s.Sort = sort);
        ReloadQuery();
    }

    public void SetAscending(bool ascending)
    {
        UpdateState(s => s.Ascending = ascending);
        ReloadQuery();
    }

    public void SetIncludeSubfolders(bool include)
    {
        UpdateState(s => s.IncludeSubfolders = include);
        ReloadQuery();
    }

    public void SetTileSide(double side) => UpdateState(s => s.TileSide = side);

    public void SetShowNames(bool show) => UpdateState(s => s.ShowNames = show);

    public void ToggleExpanded(long folderId)
    {
        if (!ExpandedFolders.Remove(folderId))
        {
            ExpandedFolders.Add(folderId);
        }
        TreesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Opens the viewer on an item of the grid.</summary>
    public void Open(long assetId)
    {
        if (Order.Contains(assetId))
        {
            OpenRequested?.Invoke(this, assetId);
        }
    }

    /// <summary>Plays a song of the grid in the mini player; anything else opens as usual.</summary>
    public void PlayInMiniPlayer(long assetId)
    {
        if (Order.IndexOf(assetId) is { } index && Refs[index].Kind == MediaKind.Audio && MiniPlayerRequested is not null)
        {
            MiniPlayerRequested(this, assetId);
            return;
        }
        Open(assetId);
    }

    // MARK: Lookup

    public Folder? Folder(long id) => _trees.Values.Select(tree => tree.Find(id)?.Folder).FirstOrDefault(f => f is not null);

    /// <summary>Folders from the root down to <paramref name="id"/>, for the breadcrumb.</summary>
    public IReadOnlyList<Folder> PathTo(long id) =>
        _trees.Values.Select(tree => tree.PathTo(id)).FirstOrDefault(path => path is not null)?.Select(n => n.Folder).ToList() ?? [];

    public void Stop()
    {
        if (_primary is not null)
        {
            // A follower shares the main model's indexer and watches: it only stops its own queries.
            StopFollowing();
            return;
        }
        Indexer.StopAll();
        _refsWatch?.Cancel();
        _albumsWatch?.Cancel();
        foreach (var stop in _folderWatches.Values)
        {
            stop.Cancel();
        }
    }
}
