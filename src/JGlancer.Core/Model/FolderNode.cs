namespace JGlancer.Core.Model;

/// <summary>A folder with its subfolders, for the sidebar tree.</summary>
/// <param name="Children">Null (not empty) when there are no subfolders, so the tree shows no disclosure arrow.</param>
public sealed record FolderNode(Folder Folder, IReadOnlyList<FolderNode>? Children)
{
    public long Id => Folder.Id;

    /// <summary>Builds the tree of one root from its flat folder rows; children in natural name order.</summary>
    public static FolderNode? Tree(IReadOnlyList<Folder> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var byParent = folders.Where(f => f.ParentId is not null).ToLookup(f => f.ParentId!.Value);
        FolderNode Node(Folder folder)
        {
            var children = byParent[folder.Id]
                .OrderBy(f => NaturalSortKey.Make(f.Name), StringComparer.Ordinal)
                .Select(Node)
                .ToList();
            return new FolderNode(folder, children.Count == 0 ? null : children);
        }
        var top = folders.FirstOrDefault(f => f.ParentId is null);
        return top is null ? null : Node(top);
    }

    /// <summary>
    /// The folders the sidebar shows: the root, and every folder with media in it or anywhere below it;
    /// <paramref name="withMedia"/> are the ids of folders that hold media themselves.
    /// </summary>
    public static IReadOnlyList<Folder> WithMedia(IReadOnlyList<Folder> folders, IReadOnlySet<long> withMedia)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(withMedia);
        var parents = folders.ToDictionary(f => f.Id, f => f.ParentId);
        var kept = new HashSet<long>();
        foreach (var id in withMedia)
        {
            // The folder and its ancestors, up to one already kept.
            for (long? at = id; at is { } current && parents.ContainsKey(current) && kept.Add(current); at = parents[current])
            {
            }
        }
        return [.. folders.Where(f => f.ParentId is null || kept.Contains(f.Id))];
    }

    /// <summary>The node with <paramref name="id"/> in this subtree.</summary>
    public FolderNode? Find(long id)
    {
        if (Id == id)
        {
            return this;
        }
        foreach (var child in Children ?? [])
        {
            if (child.Find(id) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    /// <summary>Nodes from this one down to <paramref name="id"/> (breadcrumbs), or null if it is not in the subtree.</summary>
    public IReadOnlyList<FolderNode>? PathTo(long id)
    {
        if (Id == id)
        {
            return [this];
        }
        foreach (var child in Children ?? [])
        {
            if (child.PathTo(id) is { } path)
            {
                return [this, .. path];
            }
        }
        return null;
    }
}
