namespace JGlancer.App.Library;

/// <summary>
/// Folders pinned to the top of the sidebar (W-76): any folder of any depth, in the
/// order pinned, saved with the main pane's view state (the split view's second pane shares them).
/// </summary>
internal sealed partial class LibraryModel
{
    /// <summary>The pinned folders' ids, in the order pinned (some may be gone from disk: the sidebar skips those).</summary>
    public IReadOnlyList<long> PinnedFolders => (_primary ?? this).State.PinnedFolders;

    public bool IsPinned(long folderId) => PinnedFolders.Contains(folderId);

    /// <summary>Pins a folder to the sidebar, or unpins it.</summary>
    public void TogglePin(long folderId)
    {
        var owner = _primary ?? this;
        var pinned = owner.State.PinnedFolders;
        owner.UpdateState(s => s.PinnedFolders = pinned.Contains(folderId) ? [.. pinned.Where(id => id != folderId)] : [.. pinned, folderId]);
        owner.TreesChanged?.Invoke(owner, EventArgs.Empty);
    }

    /// <summary>Shows a folder from the pinned list: its parents open in the tree, so its row is there too.</summary>
    public void RevealFolder(long folderId)
    {
        var owner = _primary ?? this;
        var path = owner.PathTo(folderId);
        foreach (var parent in path.Take(path.Count - 1))
        {
            owner.ExpandedFolders.Add(parent.Id);
        }
        SelectFolder(folderId);
        owner.TreesChanged?.Invoke(owner, EventArgs.Empty);
    }
}
