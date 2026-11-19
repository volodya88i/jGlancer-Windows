namespace JGlancer.Core.Model;

/// <summary>A row of the list view: a folder that unfolds, or a file.</summary>
public readonly record struct ListRow(bool IsFolder, long Id);

/// <summary>A row as shown: its place in the outline and whether it can unfold.</summary>
/// <param name="Depth">0 for the top level, one more inside each unfolded folder row.</param>
public readonly record struct VisibleListRow(ListRow Row, int Depth, bool IsExpanded);

/// <summary>
/// The rows of the library's list view (macOS ListOutline, D-125), as Explorer's and Finder's lists show a folder: the
/// files of the folder shown and, when subfolders are included, a row for each subfolder that holds files of the query
/// (in it or below), which unfolds to its own subfolders and files. Files keep the query's order (its sort and
/// filters); folders come first in natural name order, reversed by a descending sort. Albums, searches of the whole
/// library and a folder without its subfolders are flat lists.
/// </summary>
public sealed class ListOutline
{
    private readonly Dictionary<long, List<ListRow>> _rows = [];
    private readonly List<ListRow> _top = [];
    private readonly Dictionary<long, long> _assetFolders = [];
    private readonly Dictionary<long, long?> _folderParents = [];
    private readonly Dictionary<long, Folder> _folders = [];

    /// <summary>An empty list.</summary>
    public ListOutline()
    {
    }

    /// <param name="refs">The files of the query, in its order.</param>
    /// <param name="shown">The folder shown with its subfolders (the sidebar's tree), null for a flat list.</param>
    /// <param name="foldersDescending">Folders in reverse name order (a descending sort).</param>
    public ListOutline(IReadOnlyList<AssetRef> refs, FolderNode? shown, bool foldersDescending = false)
    {
        ArgumentNullException.ThrowIfNull(refs);
        if (shown is null)
        {
            _top.AddRange(refs.Select(r => new ListRow(false, r.Id)));
            return;
        }
        // Every folder below the one shown, with its parent (null for the shown folder's own subfolders).
        var parents = new Dictionary<long, long?>();
        var byId = new Dictionary<long, Folder>();
        var ordered = new List<FolderNode>();
        void Collect(FolderNode node, long? parent)
        {
            foreach (var child in node.Children ?? [])
            {
                parents[child.Id] = parent;
                byId[child.Id] = child.Folder;
                ordered.Add(child);
                Collect(child, child.Id);
            }
        }
        Collect(shown, null);
        var used = new HashSet<long>();
        foreach (var item in refs)
        {
            if (item.FolderId == shown.Id || !parents.ContainsKey(item.FolderId))
            {
                // In the folder shown (or in one the tree does not know yet): a top-level row.
                _top.Add(new ListRow(false, item.Id));
                continue;
            }
            Rows(item.FolderId).Add(new ListRow(false, item.Id));
            _assetFolders[item.Id] = item.FolderId;
            for (long? folder = item.FolderId; folder is { } current && used.Add(current); folder = parents[current])
            {
            }
        }
        // Folders first, in the tree's natural order (each parent's children are already sorted).
        var shownFolders = (foldersDescending ? Enumerable.Reverse(ordered) : ordered).Where(node => used.Contains(node.Id)).ToList();
        var folderRows = new Dictionary<long, List<ListRow>>();
        var topFolders = new List<ListRow>();
        foreach (var node in shownFolders)
        {
            var row = new ListRow(true, node.Id);
            if (parents[node.Id] is { } parent)
            {
                if (!folderRows.TryGetValue(parent, out var list))
                {
                    folderRows[parent] = list = [];
                }
                list.Add(row);
            }
            else
            {
                topFolders.Add(row);
            }
        }
        foreach (var (parent, list) in folderRows)
        {
            Rows(parent).InsertRange(0, list);
        }
        _top.InsertRange(0, topFolders);
        foreach (var id in used)
        {
            _folders[id] = byId[id];
            _folderParents[id] = parents[id];
        }
    }

    /// <summary>The folder of every folder row.</summary>
    public IReadOnlyDictionary<long, Folder> Folders => _folders;

    /// <summary>The rows inside a folder row, or the top level for null.</summary>
    public IReadOnlyList<ListRow> Children(long? folder) =>
        folder is { } id ? _rows.GetValueOrDefault(id) ?? [] : _top;

    /// <summary>The folder rows from the top level down to the one holding <paramref name="assetId"/>; empty for a top-level file.</summary>
    public IReadOnlyList<long> FolderPath(long assetId)
    {
        var path = new List<long>();
        for (long? folder = _assetFolders.TryGetValue(assetId, out var at) ? at : null; folder is { } current;
            folder = _folderParents.GetValueOrDefault(current))
        {
            path.Insert(0, current);
        }
        return path;
    }

    /// <summary>The rows on screen: the top level, and the rows of every unfolded folder under it.</summary>
    public IReadOnlyList<VisibleListRow> Visible(IReadOnlySet<long> expanded)
    {
        ArgumentNullException.ThrowIfNull(expanded);
        var rows = new List<VisibleListRow>();
        void Add(IReadOnlyList<ListRow> level, int depth)
        {
            foreach (var row in level)
            {
                var open = row.IsFolder && expanded.Contains(row.Id);
                rows.Add(new VisibleListRow(row, depth, open));
                if (open)
                {
                    Add(Children(row.Id), depth + 1);
                }
            }
        }
        Add(_top, 0);
        return rows;
    }

    private List<ListRow> Rows(long folder)
    {
        if (!_rows.TryGetValue(folder, out var list))
        {
            _rows[folder] = list = [];
        }
        return list;
    }
}

/// <summary>The list view's sizes (macOS D-125).</summary>
public static class ListLayout
{
    /// <summary>The size ruler sets the row height within this range while the list shows.</summary>
    public const double MinRowHeight = 22;

    public const double MaxRowHeight = 72;

    public const double DefaultRowHeight = 28;

    /// <summary>The columns after Name, as a new list shows them.</summary>
    public static IReadOnlyList<ListColumnWidth> DefaultColumns { get; } =
    [
        new(ListColumn.DateModified, 170),
        new(ListColumn.Size, 90),
        new(ListColumn.Kind, 150),
    ];

    /// <summary>The narrowest any column gets when dragged.</summary>
    public const double MinColumnWidth = 60;
}
