using Avalonia;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.Library;

/// <summary>One row of the sidebar folder tree (flattened for display; folder counts are small).</summary>
internal sealed record SidebarRow(
    long Id, long RootId, string Name, int Depth, bool IsRoot, bool HasChildren, bool IsExpanded, bool IsSelected, bool IsOffline,
    bool IsPinned = false)
{
    /// <summary>Left padding: one small step per level (macOS: depth × 12).</summary>
    public Thickness Indent => new(Depth * JGSpacing.S, 0, 0, 0);

    public string Icon => IsRoot ? JGIconData.HardDrive : JGIconData.Folder;

    public string Chevron => IsExpanded ? JGIconData.ChevronDown : JGIconData.ChevronRight;

    /// <summary>Flattens the trees of <paramref name="roots"/> in sidebar order, children of expanded folders only.</summary>
    public static List<SidebarRow> Build(
        IReadOnlyList<RootFolder> roots, IReadOnlyDictionary<long, FolderNode> trees, IReadOnlySet<long> expanded,
        long? selected, IReadOnlySet<long> offline, IReadOnlySet<long>? pinned = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(trees);
        var rows = new List<SidebarRow>();
        void Add(FolderNode node, long rootId, int depth)
        {
            var hasChildren = node.Children is not null;
            var isExpanded = hasChildren && expanded.Contains(node.Id);
            rows.Add(new SidebarRow(node.Id, rootId, node.Folder.Name, depth, depth == 0, hasChildren, isExpanded,
                node.Id == selected, depth == 0 && offline.Contains(rootId), pinned?.Contains(node.Id) == true));
            if (isExpanded)
            {
                foreach (var child in node.Children!)
                {
                    Add(child, rootId, depth + 1);
                }
            }
        }
        foreach (var root in roots)
        {
            if (trees.TryGetValue(root.Id, out var tree))
            {
                Add(tree, root.Id, 0);
            }
        }
        return rows;
    }
}
