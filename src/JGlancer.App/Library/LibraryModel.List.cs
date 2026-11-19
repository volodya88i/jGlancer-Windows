using JGlancer.Core.Model;

namespace JGlancer.App.Library;

/// <summary>The list view's state (macOS D-125, MAC-041): grid or list, the row height, the columns.</summary>
internal sealed partial class LibraryModel
{
    /// <summary>Folders unfolded in the list, for the session (by folder id).</summary>
    public HashSet<long> ListExpanded { get; } = [];

    public bool ShowsList => State.ViewMode == LibraryViewMode.List;

    public void SetViewMode(LibraryViewMode mode)
    {
        if (State.ViewMode != mode)
        {
            UpdateState(s => s.ViewMode = mode);
        }
    }

    /// <summary>The size ruler while the list shows.</summary>
    public void SetListRowHeight(double height) =>
        UpdateState(s => s.ListRowHeight = Math.Clamp(height, ListLayout.MinRowHeight, ListLayout.MaxRowHeight));

    public void SetListColumns(IReadOnlyList<ListColumnWidth> columns) => UpdateState(s => s.ListColumns = columns);

    /// <summary>
    /// The folder shown with its subfolders, when the list nests them: subfolders included, not an album, not a search
    /// of the whole library; null for a flat list.
    /// </summary>
    public FolderNode? ListTree =>
        State.IncludeSubfolders && State.AlbumId is null && !(IsSearching && SearchEverywhere) && State.FolderId is { } folder
            ? Trees.Values.Select(tree => tree.Find(folder)).FirstOrDefault(node => node is not null)
            : null;
}
