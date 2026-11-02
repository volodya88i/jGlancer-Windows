using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using JGlancer.App.Support;
using JGlancer.Core.Localization;

namespace JGlancer.App.Library;

/// <summary>The sidebar: folder trees of the library roots; ↑↓ move, ←→ collapse and expand.</summary>
internal sealed partial class SidebarView : UserControl
{
    private LibraryModel? _model;

    public SidebarView()
    {
        InitializeComponent();
        AddButton.Click += async (_, _) => await AddFolderAsync();
        Rows.KeyDown += OnRowsKeyDown;
        DragDrop.SetAllowDrop(Rows, true);
        Rows.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        Rows.AddHandler(DragDrop.DragLeaveEvent, (_, _) => Highlight(null));
        Rows.AddHandler(DragDrop.DropEvent, OnDrop);
        Rows.AddHandler(PointerPressedEvent, OnRowPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        Rows.AddHandler(PointerMovedEvent, OnRowMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        Rows.AddHandler(PointerReleasedEvent, (_, _) => _rowPress = null, RoutingStrategies.Tunnel, handledEventsToo: true);
        AttachAlbums();
        ToolTip.SetTip(TrashButton, L.T("Show the Recycle Bin in Explorer; drop files here to move them to the Recycle Bin"));
        TrashButton.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });
        TrashButton.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            var files = DroppedFiles(e);
            e.DragEffects = files.Count > 0 && TrashFiles is not null ? DragDropEffects.Move : DragDropEffects.None;
            Highlight(files.Count > 0 ? TrashButton : null);
            e.Handled = true;
        });
        TrashButton.AddHandler(DragDrop.DragLeaveEvent, (_, _) => Highlight(null));
        TrashButton.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            Highlight(null);
            var files = DroppedFiles(e);
            e.Handled = true;
            if (files.Count > 0 && TrashFiles is { } trash)
            {
                e.DragEffects = DragDropEffects.Move;
                await trash(files);
            }
        });
    }

    /// <summary>Sends files to the Recycle Bin (set by the window: file operations, undoable).</summary>
    public Func<IReadOnlyList<string>, Task>? TrashFiles { get; set; }

    /// <summary>Moves (<c>false</c>) or copies (<c>true</c>) files into a folder (set by the window: file operations).</summary>
    public Func<IReadOnlyList<string>, string, bool, Task>? DropFiles { get; set; }

    private Button? _dropTarget;

    /// <summary>The folder row under the pointer and its path on disk, if it can take files.</summary>
    private (Button Row, string Folder)? DropTarget(DragEventArgs e)
    {
        if (_model is null || e.Source is not Avalonia.Visual source
            || Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Button>(source, includeSelf: true) is not { Tag: SidebarRow row } button
            || row.IsOffline || _model.Folder(row.Id) is not { } folder
            || _model.Roots.FirstOrDefault(r => r.Id == row.RootId) is not { } root)
        {
            return null;
        }
        return (button, Core.Store.LibraryPaths.Absolute(root.Path, folder.RelativePath));
    }

    private static List<string> DroppedFiles(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.OfType<Avalonia.Platform.Storage.IStorageFile>()
            .Select(file => Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(file))
            .OfType<string>()
            .ToList() ?? [];

    /// <summary>As Explorer: Ctrl copies, Shift moves; otherwise a move on the same drive and a copy to another.</summary>
    private static bool Copies(DragEventArgs e, List<string> files, string folder) =>
        e.KeyModifiers.HasFlag(KeyModifiers.Control)
        || (!e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            && !string.Equals(Path.GetPathRoot(files[0]), Path.GetPathRoot(folder), StringComparison.OrdinalIgnoreCase));

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var target = DropTarget(e);
        var files = DroppedFiles(e);
        if (target is not { } place || files.Count == 0 || DropFiles is null)
        {
            e.DragEffects = DragDropEffects.None;
            Highlight(null);
        }
        else
        {
            e.DragEffects = Copies(e, files, place.Folder) ? DragDropEffects.Copy : DragDropEffects.Move;
            Highlight(place.Row);
        }
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        Highlight(null);
        var target = DropTarget(e);
        var files = DroppedFiles(e);
        e.Handled = true;
        if (target is { } place && files.Count > 0 && DropFiles is { } drop)
        {
            var copying = Copies(e, files, place.Folder);
            e.DragEffects = copying ? DragDropEffects.Copy : DragDropEffects.Move;
            await drop(files, place.Folder, copying);
        }
    }

    private void Highlight(Button? row)
    {
        if (_dropTarget == row)
        {
            return;
        }
        _dropTarget?.Classes.Remove("dropTarget");
        _dropTarget = row;
        _dropTarget?.Classes.Add("dropTarget");
    }

    public LibraryModel? Model
    {
        get => _model;
        set
        {
            if (_model is not null)
            {
                _model.TreesChanged -= OnTreesChanged;
                _model.AlbumsChanged -= OnAlbumsChanged;
            }
            _model = value;
            if (_model is not null)
            {
                _model.TreesChanged += OnTreesChanged;
                _model.AlbumsChanged += OnAlbumsChanged;
            }
            Rebuild();
        }
    }

    private List<SidebarRow> CurrentRows => Rows.ItemsSource as List<SidebarRow> ?? [];

    private void OnTreesChanged(object? sender, EventArgs e) => Rebuild();

    /// <summary>The model of the pane folders and albums open in (the split view's active pane); the main one by default.</summary>
    public Func<LibraryModel?>? ActiveModel { get; set; }

    private LibraryModel Shown => ActiveModel?.Invoke() ?? _model!;

    /// <summary>The active pane changed or showed another folder: the highlight follows.</summary>
    public void Refresh() => Rebuild();

    private (PointerPressedEventArgs Press, Point At, long Folder)? _rowPress;
    private bool _rowDragged;

    /// <summary>A folder row can be dragged onto a pane, which then shows it (split view).</summary>
    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        _rowDragged = false;
        _rowPress = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && RowAt(e.Source) is { } row
            ? (e, e.GetPosition(this), row.Id)
            : null;
    }

    private async void OnRowMoved(object? sender, PointerEventArgs e)
    {
        if (_rowPress is not { } press)
        {
            return;
        }
        var at = e.GetPosition(this);
        if (Math.Abs(at.X - press.At.X) + Math.Abs(at.Y - press.At.Y) < JGlancer.Design.JGMetrics.DragThreshold)
        {
            return;
        }
        _rowPress = null;
        _rowDragged = true;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(BrowserPane.FolderFormat, press.Folder.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        await DragDrop.DoDragDropAsync(press.Press, data, DragDropEffects.Move);
    }

    private static SidebarRow? RowAt(object? source) =>
        source is Avalonia.Visual visual && Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Button>(visual, includeSelf: true) is { Tag: SidebarRow row }
            ? row
            : null;

    private void OnAlbumsChanged(object? sender, EventArgs e) => RebuildAlbums();

    private void Rebuild()
    {
        if (_model is null)
        {
            Rows.ItemsSource = null;
            RebuildPins();
            RebuildAlbums();
            return;
        }
        // An album shown: no folder is the selected one. The highlight is the active pane's (split view).
        var shown = Shown;
        Rows.ItemsSource = SidebarRow.Build(_model.Roots, _model.Trees, _model.ExpandedFolders,
            shown.State.AlbumId is null ? shown.State.FolderId : null, _model.Indexer.OfflineRoots, _model.PinnedFolders.ToHashSet());
        RebuildPins();
        RebuildAlbums();
    }

    private async Task AddFolderAsync()
    {
        if (_model is not null && TopLevel.GetTopLevel(this) is { } top)
        {
            await _model.AddFoldersAsync(await FolderPicker.ChooseAsync(top));
        }
    }

    private void OnRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: SidebarRow row } && !_rowDragged)
        {
            Shown.SelectFolder(row.Id);
        }
    }

    private void OnChevronClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: SidebarRow row })
        {
            _model?.ToggleExpanded(row.Id);
            e.Handled = true;
        }
    }

    private async void OnRemoveRoot(object? sender, RoutedEventArgs e)
    {
        if (_model is null || sender is not Control { Tag: SidebarRow row } || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }
        // Removing a root asks first until undo exists (macOS D-35); files are never touched.
        var confirmed = await ConfirmDialog.ShowAsync(owner, L.T("Remove “{0}” from the library?", row.Name),
            L.T("Files stay where they are."), L.T("Remove"));
        if (confirmed)
        {
            await _model.RemoveRootAsync(row.RootId);
        }
    }

    /// <summary>Makes a folder inside a folder on disk (named first; undoable).</summary>
    public Func<string, string, Task>? CreateFolder { get; set; }

    /// <summary>Sends a folder to the Recycle Bin (undoable).</summary>
    public Func<string, Task>? TrashFolder { get; set; }

    /// <summary>The folder of <paramref name="row"/> on disk.</summary>
    private string? PathOf(SidebarRow row) =>
        _model?.Folder(row.Id) is { } folder && _model.Roots.FirstOrDefault(r => r.Id == row.RootId) is { } root
            ? Core.Store.LibraryPaths.Absolute(root.Path, folder.RelativePath)
            : null;

    private async void OnNewFolder(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: SidebarRow row } || PathOf(row) is not { } parent || CreateFolder is not { } create
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }
        if (await NameDialog.AskAsync(owner, L.T("New Folder"), L.T("New Folder"), L.T("Create")) is { Length: > 0 } name)
        {
            _model?.ExpandedFolders.Add(row.Id);
            await create(parent, name);
        }
    }

    private void OnShowFolder(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: SidebarRow row } && PathOf(row) is { } path)
        {
            Platform.Windows.ShellFiles.ShowInExplorer([path]);
        }
    }

    private async void OnTrashFolder(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: SidebarRow row })
        {
            await TrashFolderAsync(row);
        }
    }

    /// <summary>A subfolder to the Recycle Bin; the folder shown moves up to its parent first if it was in it.</summary>
    private async Task TrashFolderAsync(SidebarRow row)
    {
        if (row.IsRoot || row.IsOffline || _model is not { } model || PathOf(row) is not { } path || TrashFolder is not { } trash)
        {
            return;
        }
        if (model.State.FolderId is { } shown && model.PathTo(shown).Any(f => f.Id == row.Id) && model.Folder(row.Id)?.ParentId is { } parent)
        {
            model.SelectFolder(parent);
        }
        await trash(path);
    }

    private async void OnRowsKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is null)
        {
            return;
        }
        var rows = CurrentRows;
        if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None && rows.Find(r => r.IsSelected) is { IsRoot: false } selected)
        {
            e.Handled = true;
            await TrashFolderAsync(selected);
            return;
        }
        var current = rows.FindIndex(r => r.IsSelected);
        if (current < 0)
        {
            if (rows.Count > 0 && e.Key is Key.Down or Key.Up)
            {
                _model.SelectFolder(rows[0].Id);
                e.Handled = true;
            }
            return;
        }
        switch (e.Key)
        {
            case Key.Up when current > 0:
                _model.SelectFolder(rows[current - 1].Id);
                break;
            case Key.Down when current < rows.Count - 1:
                _model.SelectFolder(rows[current + 1].Id);
                break;
            case Key.Left when rows[current].IsExpanded:
            case Key.Right when rows[current].HasChildren && !rows[current].IsExpanded:
                _model.ToggleExpanded(rows[current].Id);
                break;
            default:
                return;
        }
        e.Handled = true;
    }
}
