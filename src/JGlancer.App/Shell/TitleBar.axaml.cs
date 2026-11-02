using Avalonia;
using Avalonia.Controls;
using JGlancer.App.Developer;
using JGlancer.App.Library;
using JGlancer.Core.Localization;

namespace JGlancer.App.Shell;

/// <summary>The window's own title bar: menus and caption buttons (W-04).</summary>
internal sealed partial class TitleBar : UserControl
{
    private const string MaximizeGlyph = "";
    private const string RestoreGlyph = "";
    private Window? _window;
    private LibraryModel? _model;

    public TitleBar()
    {
        InitializeComponent();
        MinimizeButton.Click += (_, _) => Minimize();
        MinimizeItem.Click += (_, _) => Minimize();
        MaximizeButton.Click += (_, _) => ToggleMaximize();
        MaximizeItem.Click += (_, _) => ToggleMaximize();
        CloseButton.Click += (_, _) => _window?.Close();
        ExitItem.Click += (_, _) => _window?.Close();
        AddFolderItem.Click += (_, _) => AddFolderRequested?.Invoke(this, EventArgs.Empty);
        ShowNamesItem.Click += (_, _) => Model?.SetShowNames(!Model.State.ShowNames);
        SubfoldersItem.Click += (_, _) => Model?.SetIncludeSubfolders(!Model.State.IncludeSubfolders);
        ShowPreviewItem.Click += (_, _) => Primary?.SetPreviewVisible(!Primary.State.PreviewVisible);
        SplitOffItem.Click += (_, _) => SplitRequested?.Invoke(this, Core.Model.SplitMode.Off);
        SplitSideItem.Click += (_, _) => SplitRequested?.Invoke(this, Core.Model.SplitMode.SideBySide);
        SplitStackedItem.Click += (_, _) => SplitRequested?.Invoke(this, Core.Model.SplitMode.Stacked);
        NewAlbumItem.Click += async (_, _) =>
        {
            if (Model is { HasRoots: true } model)
            {
                await model.CreateAlbumAsync(model.SelectedInOrder());
            }
        };
        QuickPreviewItem.Click += (_, _) => QuickPreviewRequested?.Invoke(this, EventArgs.Empty);
        SettingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        FindItem.Click += (_, _) => Model?.RequestSearchFocus();
        DuplicatesItem.Click += (_, _) => DuplicatesRequested?.Invoke(this, EventArgs.Empty);
        AboutItem.Click += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window owner)
            {
                _ = new AboutWindow().ShowDialog(owner);
            }
        };
        ViewMenu.SubmenuOpened += (_, _) =>
        {
            QuickPreviewItem.IsChecked = IsQuickPreviewOpen?.Invoke() == true;
            var split = CurrentSplit?.Invoke() ?? Core.Model.SplitMode.Off;
            SplitOffItem.IsChecked = split == Core.Model.SplitMode.Off;
            SplitSideItem.IsChecked = split == Core.Model.SplitMode.SideBySide;
            SplitStackedItem.IsChecked = split == Core.Model.SplitMode.Stacked;
            UpdateChecks();
        };
        ApplyShortcuts();
    }

    /// <summary>The hints of the commands Settings → Shortcuts can change (the window runs the keys).</summary>
    private void ApplyShortcuts()
    {
        var map = Settings.SettingsStore.Current.Shortcuts;
        (MenuItem Item, Core.Actions.ShortcutCommand Command)[] items =
        [
            (AddFolderItem, Core.Actions.ShortcutCommand.AddFolder), (NewAlbumItem, Core.Actions.ShortcutCommand.NewAlbum),
            (EditInItem, Core.Actions.ShortcutCommand.EditInPrimaryApp), (GetInfoItem, Core.Actions.ShortcutCommand.GetInfo),
            (ShowInExplorerItem, Core.Actions.ShortcutCommand.ShowInExplorer), (CopyPathItem, Core.Actions.ShortcutCommand.CopyPath),
            (MoveToItem, Core.Actions.ShortcutCommand.MoveTo), (CopyToItem, Core.Actions.ShortcutCommand.CopyTo),
            (RenameItem, Core.Actions.ShortcutCommand.Rename), (TrashItem, Core.Actions.ShortcutCommand.MoveToTrash),
            (ShowPreviewItem, Core.Actions.ShortcutCommand.TogglePreview), (ShowNamesItem, Core.Actions.ShortcutCommand.ShowNames),
            (SubfoldersItem, Core.Actions.ShortcutCommand.IncludeSubfolders), (FindItem, Core.Actions.ShortcutCommand.Find),
            (SplitOffItem, Core.Actions.ShortcutCommand.SplitView), (SplitSideItem, Core.Actions.ShortcutCommand.SplitSideBySide),
            (SplitStackedItem, Core.Actions.ShortcutCommand.SplitStacked),
        ];
        foreach (var (item, command) in items)
        {
            item.InputGesture = Actions.ActionMenu.Gesture(map.Shortcut(command));
        }
    }

    private void OnShortcutsChanged(object? sender, EventArgs e) => ApplyShortcuts();

    private Func<Actions.ActionTarget?>? _target;

    /// <summary>
    /// The registry's actions in File and Edit (macOS ActionMenuItems): titles and availability follow the current
    /// target each time a menu opens; Open With stays in the context menu, where its programs are looked up.
    /// </summary>
    public void AttachActions(Actions.ActionPerformer actions, Func<Actions.ActionTarget?> target, Action selectAll)
    {
        _target = target;
        (MenuItem Item, Core.Actions.ActionId Id)[] items =
        [
            (OpenItem, Core.Actions.ActionId.Open), (EditInItem, Core.Actions.ActionId.EditInPrimaryApp),
            (GetInfoItem, Core.Actions.ActionId.GetInfo), (ShowInExplorerItem, Core.Actions.ActionId.ShowInExplorer),
            (CopyPathItem, Core.Actions.ActionId.CopyPath), (CopyItem, Core.Actions.ActionId.Copy),
            (MoveToItem, Core.Actions.ActionId.MoveTo), (CopyToItem, Core.Actions.ActionId.CopyTo),
            (RenameItem, Core.Actions.ActionId.Rename), (TrashItem, Core.Actions.ActionId.MoveToTrash),
        ];
        foreach (var (item, id) in items)
        {
            item.Click += async (_, _) =>
            {
                if (_target?.Invoke() is { } now)
                {
                    await actions.PerformAsync(id, now);
                }
            };
        }
        void Refresh()
        {
            var current = target();
            foreach (var (item, id) in items)
            {
                item.Header = actions.Title(id, current);
                item.IsEnabled = actions.IsAvailable(id, current);
            }
            // Edit in … only once a primary program is set for the kind (W9).
            EditInItem.IsVisible = EditInItem.IsEnabled;
            FlagItem.Items.Clear();
            FlagItem.IsEnabled = current is not null && actions.IsAvailable(Core.Actions.ActionId.Flag, current);
            if (FlagItem.IsEnabled)
            {
                Actions.ActionMenu.AddFlags(FlagItem, current!, actions);
            }
            FindItem.IsEnabled = Model?.HasRoots == true;
            DuplicatesItem.IsEnabled = Model?.HasRoots == true;
        }
        FileMenu.SubmenuOpened += (_, _) => Refresh();
        EditMenu.SubmenuOpened += (_, _) => Refresh();
        SelectAllItem.Click += (_, _) => selectAll();
        Refresh();
    }

    /// <summary>Edit ▸ Undo … / Redo … with the operation's name; disabled while nothing can be undone or one runs.</summary>
    public void AttachUndo(FileOps.FileOperationsModel fileOps)
    {
        ArgumentNullException.ThrowIfNull(fileOps);
        UndoItem.Click += async (_, _) => await fileOps.UndoAsync();
        RedoItem.Click += async (_, _) => await fileOps.RedoAsync();
        EditMenu.SubmenuOpened += (_, _) =>
        {
            UndoItem.IsEnabled = fileOps.CanUndo;
            RedoItem.IsEnabled = fileOps.CanRedo;
            UndoItem.Header = fileOps.CanUndo ? L.T("Undo {0}", fileOps.UndoTitle) : L.T("Undo");
            RedoItem.Header = fileOps.CanRedo ? L.T("Redo {0}", fileOps.RedoTitle) : L.T("Redo");
        };
    }

    /// <summary>File ▸ Add Folder… (the library view owns the picker).</summary>
    public event EventHandler? AddFolderRequested;

    /// <summary>File ▸ Settings… (the window owns it).</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Edit ▸ Find Duplicates….</summary>
    public event EventHandler? DuplicatesRequested;

    /// <summary>View ▸ Split View ▸ a choice (the window makes or removes the second pane).</summary>
    public event EventHandler<Core.Model.SplitMode>? SplitRequested;

    /// <summary>The split view now (its menu's check).</summary>
    public Func<Core.Model.SplitMode>? CurrentSplit { get; set; }

    private LibraryModel? _primary;

    /// <summary>The main library model: the one preview pane is its (Show Preview); <see cref="Model"/> is the active pane's.</summary>
    public LibraryModel? Primary
    {
        get => _primary ?? _model;
        set => _primary = value;
    }

    /// <summary>View ▸ Quick Preview (the window owns it).</summary>
    public event EventHandler? QuickPreviewRequested;

    /// <summary>Whether Quick Preview is open: its View item is checked then.</summary>
    public Func<bool>? IsQuickPreviewOpen { get; set; }

    public LibraryModel? Model
    {
        get => _model;
        set
        {
            if (_model is not null)
            {
                _model.StateChanged -= OnStateChanged;
            }
            _model = value;
            if (_model is not null)
            {
                _model.StateChanged += OnStateChanged;
            }
            UpdateChecks();
        }
    }

    private void OnStateChanged(object? sender, EventArgs e) => UpdateChecks();

    private void UpdateChecks()
    {
        ShowNamesItem.IsChecked = _model?.State.ShowNames == true;
        SubfoldersItem.IsChecked = _model?.State.IncludeSubfolders == true;
        ShowPreviewItem.IsChecked = Primary?.State.PreviewVisible == true;
        ShowNamesItem.IsEnabled = SubfoldersItem.IsEnabled = ShowPreviewItem.IsEnabled = QuickPreviewItem.IsEnabled = _model is not null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Settings.SettingsStore.Current.ShortcutsChanged += OnShortcutsChanged;
        ApplyShortcuts();
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
        {
            _window.PropertyChanged += OnWindowPropertyChanged;
            UpdateMaximizeState();
            if (DeveloperOptions.Current.OpenMenu is { } index && index < MenuBar.ItemCount)
            {
                _window.Opened += (_, _) => (MenuBar.Items[index] as MenuItem)?.Open();
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Settings.SettingsStore.Current.ShortcutsChanged -= OnShortcutsChanged;
        if (_window is not null)
        {
            _window.PropertyChanged -= OnWindowPropertyChanged;
            _window = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
        {
            UpdateMaximizeState();
        }
    }

    private void Minimize()
    {
        if (_window is not null)
        {
            _window.WindowState = WindowState.Minimized;
        }
    }

    private void ToggleMaximize()
    {
        if (_window is not null)
        {
            _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }

    private void UpdateMaximizeState()
    {
        var maximized = _window?.WindowState == WindowState.Maximized;
        var title = L.T(maximized ? "Restore" : "Maximize");
        MaximizeButton.Content = maximized ? RestoreGlyph : MaximizeGlyph;
        ToolTip.SetTip(MaximizeButton, title);
        Avalonia.Automation.AutomationProperties.SetName(MaximizeButton, title);
        MaximizeItem.Header = title;
    }
}
