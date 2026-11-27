using JGlancer.App.Library;
using JGlancer.Core.Model;

namespace JGlancer.App.SizeMap;

/// <summary>
/// The size map's data (macOS D-127): the folder shown in the main pane and everything below it, with the main pane's
/// filters (kinds, flags, search, hidden folders, small files), as a tree of sizes. Rebuilt off the UI thread when the
/// folder or its files change, and only while the map shows.
/// </summary>
internal sealed class SizeMapModel
{
    public enum Content
    {
        Loading,
        Tree,

        /// <summary>An album is shown: the map belongs to folders.</summary>
        Album,

        /// <summary>The folder holds no files of the query.</summary>
        Empty,
    }

    private readonly LibraryModel _library;
    private CancellationTokenSource? _loading;
    private bool _isActive;
    private bool _isStale = true;

    public SizeMapModel(LibraryModel library)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
        // The files, the folder or its tree changed: the map follows (rescans and filters arrive as new files).
        library.RefsChanged += (_, _) => Invalidate();
        library.TreesChanged += (_, _) => Invalidate();
    }

    public LibraryModel Library => _library;

    public SizeTree Tree { get; private set; } = SizeTree.Empty;

    public Content State { get; private set; } = Content.Loading;

    /// <summary>A new tree or state.</summary>
    public event EventHandler? Changed;

    /// <summary>Shown or hidden: the map is made only while it shows.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            _isActive = value;
            if (value && _isStale)
            {
                Reload();
            }
            else if (!value)
            {
                _loading?.Cancel();
            }
        }
    }

    private void Invalidate()
    {
        _isStale = true;
        if (_isActive)
        {
            Reload();
        }
    }

    private void Reload()
    {
        _isStale = false;
        _loading?.Cancel();
        var state = _library.State;
        if (state.AlbumId is not null || state.FolderId is not { } folderId || FolderNode(folderId) is not { } folder)
        {
            Publish(SizeTree.Empty, state.AlbumId is not null ? Content.Album : Content.Empty);
            return;
        }
        var stop = new CancellationTokenSource();
        _loading = stop;
        var query = _library.Query with { Scope = QueryScope.InFolder(folderId, true) };
        var store = _library.Store;
        _ = Load();
        async Task Load()
        {
            try
            {
                var entries = await store.SizeEntriesAsync(query);
                var tree = await Task.Run(() => SizeTree.Build(folder, entries), stop.Token);
                if (!stop.IsCancellationRequested)
                {
                    Publish(tree, tree.IsEmpty ? Content.Empty : Content.Tree);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private void Publish(SizeTree tree, Content state)
    {
        Tree = tree;
        State = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The sidebar's node of a folder (with its subfolders).</summary>
    private FolderNode? FolderNode(long id) => _library.Trees.Values.Select(tree => tree.Find(id)).FirstOrDefault(node => node is not null);
}
