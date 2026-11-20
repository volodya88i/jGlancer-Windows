namespace JGlancer.App.Library;

/// <summary>
/// Subfolders hidden from the library (owner, 0.6.1): right-click a folder in the sidebar, Hide from Library. Its files
/// and everything below it leave the views and the sidebar; the files stay where they are and are still indexed, and
/// Settings → Library lists the hidden folders to show them again. Saved with the main pane's view state (the split
/// view's second pane shares them, as the pins).
/// </summary>
internal sealed partial class LibraryModel
{
    public IReadOnlyList<long> HiddenFolders => (_primary ?? this).State.HiddenFolders;

    /// <summary>A folder was hidden or shown again (the split view's second pane follows).</summary>
    public event EventHandler<(long FolderId, bool Hidden)>? HiddenFoldersChanged;

    /// <summary>The query again; a hidden folder that was shown gives way to its parent.</summary>
    private void FollowHidden(long folderId, bool hidden)
    {
        var path = State.FolderId is { } shown ? PathTo(shown) : [];
        var at = path.ToList().FindIndex(f => f.Id == folderId);
        if (hidden && at > 0)
        {
            SelectFolder(path[at - 1].Id);
            return;
        }
        ReloadQuery();
    }

    private void OnPrimaryHiddenChanged(object? sender, (long FolderId, bool Hidden) change) => FollowHidden(change.FolderId, change.Hidden);

    public void HideFolder(long folderId) => SetHidden(folderId, hidden: true);

    public void ShowHiddenFolder(long folderId) => SetHidden(folderId, hidden: false);

    private void SetHidden(long folderId, bool hidden)
    {
        var owner = _primary ?? this;
        var current = owner.State.HiddenFolders;
        if (current.Contains(folderId) == hidden)
        {
            return;
        }
        owner.UpdateState(s => s.HiddenFolders = hidden ? [.. current, folderId] : [.. current.Where(id => id != folderId)]);
        owner.FollowHidden(folderId, hidden);
        owner.HiddenFoldersChanged?.Invoke(owner, (folderId, hidden));
        owner.TreesChanged?.Invoke(owner, EventArgs.Empty);
    }
}
