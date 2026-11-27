namespace JGlancer.Core.Model;

/// <summary>One file of the size map (macOS D-127): what the library knows of it.</summary>
public sealed record SizeEntry(long Id, long FolderId, long Size, MediaKind Kind, string Name)
{
    /// <summary>The lowercased extension without its dot, for the color's shade.</summary>
    public string Extension { get; } = System.IO.Path.GetExtension(Name).TrimStart('.').ToLowerInvariant();
}

/// <summary>
/// The folder shown and everything below it as a tree of sizes (macOS D-127, like WizTree): folders hold their
/// subfolders and files, each folder's size is the sum of what it holds, children come largest first. Folders and
/// 0-byte files without anything to show are left out. Stored flat (<see cref="Nodes"/>, the folder shown at 0) so a
/// layout can index it.
/// </summary>
public sealed class SizeTree
{
    /// <summary>A folder or a file of the tree.</summary>
    /// <param name="IsFolder">A folder (its <see cref="Id"/> a folder id) or a file (an asset id).</param>
    /// <param name="Kind">The file's kind; null for folders.</param>
    /// <param name="Children">Indices into <see cref="Nodes"/>, largest first.</param>
    public sealed record Node(bool IsFolder, long Id, string Name, long Size, MediaKind? Kind, string Extension, IReadOnlyList<int> Children);

    /// <summary>The tree without nodes.</summary>
    public static SizeTree Empty { get; } = new([]);

    private SizeTree(IReadOnlyList<Node> nodes) => Nodes = nodes;

    /// <summary>Every node; the folder shown at 0.</summary>
    public IReadOnlyList<Node> Nodes { get; }

    public bool IsEmpty => Nodes.Count == 0;

    /// <summary>The folder shown.</summary>
    public Node? Root => Nodes.Count > 0 ? Nodes[0] : null;

    /// <summary>The tree of <paramref name="folder"/> (the sidebar's node, with its subfolders) over the query's <paramref name="files"/>.</summary>
    public static SizeTree Build(FolderNode folder, IReadOnlyList<SizeEntry> files)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(files);
        var byFolder = files.ToLookup(file => file.FolderId);
        var nodes = new List<Node>();
        // Builds the node's subtree into `nodes` (children first) and returns its index, or -1 when it holds nothing.
        int Add(FolderNode at)
        {
            var children = new List<(int Index, long Size)>();
            foreach (var child in at.Children ?? [])
            {
                if (Add(child) is var index and >= 0)
                {
                    children.Add((index, nodes[index].Size));
                }
            }
            foreach (var file in byFolder[at.Id])
            {
                if (file.Size > 0)
                {
                    nodes.Add(new Node(false, file.Id, file.Name, file.Size, file.Kind, file.Extension, []));
                    children.Add((nodes.Count - 1, file.Size));
                }
            }
            var size = children.Sum(child => child.Size);
            if (size <= 0)
            {
                return -1;
            }
            children.Sort((a, b) => a.Size == b.Size ? a.Index.CompareTo(b.Index) : b.Size.CompareTo(a.Size));
            nodes.Add(new Node(true, at.Id, at.Folder.Name, size, null, "", [.. children.Select(child => child.Index)]));
            return nodes.Count - 1;
        }
        var root = Add(folder);
        if (root < 0)
        {
            return Empty;
        }
        // The root goes first: indices are remapped so that Nodes[0] is the folder shown.
        var order = Enumerable.Range(0, nodes.Count).ToArray();
        (order[0], order[root]) = (order[root], order[0]);
        var position = new int[nodes.Count];
        for (var i = 0; i < order.Length; i++)
        {
            position[order[i]] = i;
        }
        return new SizeTree([.. order.Select(old => nodes[old] with { Children = [.. nodes[old].Children.Select(c => position[c])] })]);
    }

    /// <summary>The index of each file's node, by asset id.</summary>
    public IReadOnlyDictionary<long, int> FileNodes()
    {
        var files = new Dictionary<long, int>();
        for (var i = 0; i < Nodes.Count; i++)
        {
            if (!Nodes[i].IsFolder)
            {
                files.TryAdd(Nodes[i].Id, i);
            }
        }
        return files;
    }
}
