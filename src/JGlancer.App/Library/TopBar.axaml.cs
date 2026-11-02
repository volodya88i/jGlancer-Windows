using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.Library;

/// <summary>The library's top bar: where you are, what kinds, in what order, how big.</summary>
internal sealed partial class TopBar : UserControl
{
    private LibraryModel? _model;
    private bool _updating;

    public TopBar()
    {
        InitializeComponent();
        VideoChip.Click += (_, _) => Toggle(MediaKind.Video);
        PhotoChip.Click += (_, _) => Toggle(MediaKind.Photo);
        AudioChip.Click += (_, _) => Toggle(MediaKind.Audio);
        VideoIcon.Click += (_, _) => Toggle(MediaKind.Video);
        PhotoIcon.Click += (_, _) => Toggle(MediaKind.Photo);
        AudioIcon.Click += (_, _) => Toggle(MediaKind.Audio);
        SortButton.Click += (_, _) => ShowSortMenu();
        FlagFilterButton.Click += (_, _) => ShowFlagMenu();
        SearchButton.Click += (_, _) => OpenSearch();
        ClearSearchButton.Click += (_, _) => CloseSearch();
        EverywhereChip.Click += (_, _) =>
        {
            _model?.SetSearchEverywhere(EverywhereChip.IsChecked == true);
            // Back to typing.
            SearchText.Focus();
        };
        SearchText.TextChanged += (_, _) =>
        {
            if (!_updating)
            {
                _model?.SetSearch(SearchText.Text ?? "");
            }
        };
        SearchText.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
            {
                e.Handled = true;
                CloseSearch();
            }
            else if (e.Key == Avalonia.Input.Key.Enter)
            {
                e.Handled = true;
                ReturnFocusRequested?.Invoke(this, EventArgs.Empty);
            }
        };
        // An empty field folds back into the magnifier once it loses the keyboard.
        SearchText.LostFocus += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_model?.IsSearching != true && !SearchText.IsFocused && !EverywhereChip.IsFocused && !ClearSearchButton.IsFocused)
            {
                _searchOpen = false;
                UpdateSearch();
            }
        }, Avalonia.Threading.DispatcherPriority.Input);
        SizeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == JGSlider.ValueProperty && !_updating && _model is not null && _model.State.TileSide != SizeSlider.Value)
            {
                _model.SetTileSide(SizeSlider.Value);
            }
        };
    }

    public LibraryModel? Model
    {
        get => _model;
        set
        {
            if (_model is not null)
            {
                _model.StateChanged -= OnChanged;
                _model.TreesChanged -= OnChanged;
                _model.SearchFocusRequested -= OnSearchFocusRequested;
            }
            _model = value;
            if (_model is not null)
            {
                _model.StateChanged += OnChanged;
                _model.TreesChanged += OnChanged;
                _model.SearchFocusRequested += OnSearchFocusRequested;
            }
            Update();
        }
    }

    private void OnChanged(object? sender, EventArgs e) => Update();

    private bool _showsSizeSlider = true;

    /// <summary>The size slider; off in the split view (Ctrl+wheel sizes each pane's tiles).</summary>
    public bool ShowsSizeSlider
    {
        get => _showsSizeSlider;
        set
        {
            _showsSizeSlider = value;
            Fit();
        }
    }

    /// <summary>Enter or Esc in the search field: the grid takes the keyboard again.</summary>
    public event EventHandler? ReturnFocusRequested;

    private bool _searchOpen;

    private void OnSearchFocusRequested(object? sender, EventArgs e) => OpenSearch();

    private void OpenSearch()
    {
        _searchOpen = true;
        UpdateSearch();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            SearchText.Focus();
            SearchText.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Input);
    }

    private void CloseSearch()
    {
        _searchOpen = false;
        _model?.EndSearch();
        UpdateSearch();
        ReturnFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The magnifier, or the field with Everywhere while it is open or a search is on.</summary>
    private void UpdateSearch()
    {
        var searching = _model?.IsSearching == true;
        var open = _searchOpen || searching;
        SearchButton.IsVisible = !open;
        SearchField.IsVisible = EverywhereChip.IsVisible = open;
        ClearSearchButton.IsVisible = searching;
        var updating = _updating;
        _updating = true;
        if (_model is not null && (SearchText.Text ?? "") != _model.SearchText)
        {
            SearchText.Text = _model.SearchText;
        }
        EverywhereChip.IsChecked = _model?.SearchEverywhere == true;
        _updating = updating;
        // The open field takes room: the slider, then the sort menu step aside if they must.
        Fit();
    }

    /// <summary>The star: outline in the secondary text color when off, filled in the flag's color when on.</summary>
    private void UpdateFlagFilter()
    {
        var filter = _model?.State.FlagFilter;
        FlagFilterGlyph.Content = filter is null
            ? new JGIcon { Data = JGIconData.Star, Foreground = this.Brush(JGColor.TextSecondary) }
            : FlagStyle.Glyph(this, filter.Only ?? AssetFlag.Accent, JGMetrics.IconSize);
    }

    /// <summary>All Files, All Flagged, then each flag (macOS FlagFilterMenu).</summary>
    private void ShowFlagMenu()
    {
        if (_model is null)
        {
            return;
        }
        var current = _model.State.FlagFilter;
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        menu.Items.Add(Radio(L.T("All Files"), "flags", current is null, () => _model.SetFlagFilter(null)));
        menu.Items.Add(Radio(L.T("All Flagged"), "flags", current == FlagFilter.Flagged, () => _model.SetFlagFilter(FlagFilter.Flagged)));
        menu.Items.Add(new Separator());
        foreach (var flag in FlagStyle.MenuOrder.Where(f => f != AssetFlag.None))
        {
            var item = Radio(FlagStyle.Title(flag), "flags", current?.Only == flag, () => _model.SetFlagFilter(new FlagFilter(flag)));
            item.Icon = FlagStyle.Glyph(this, flag, JGMetrics.IconSize);
            menu.Items.Add(item);
        }
        menu.ShowAt(FlagFilterButton);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Settings.SettingsStore.Current.Changed += OnSettingsChanged;
        Fit();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Settings.SettingsStore.Current.Changed -= OnSettingsChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnSettingsChanged(object? sender, (Core.Settings.AppSettings Old, Core.Settings.AppSettings New) change) => Fit();

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Fit();
    }

    private void Update()
    {
        if (_model is null)
        {
            return;
        }
        _updating = true;
        var state = _model.State;
        Controls.IsVisible = _model.HasRoots;
        VideoChip.IsChecked = VideoIcon.IsChecked = state.KindSet.Contains(MediaKind.Video);
        PhotoChip.IsChecked = PhotoIcon.IsChecked = state.KindSet.Contains(MediaKind.Photo);
        AudioChip.IsChecked = AudioIcon.IsChecked = state.KindSet.Contains(MediaKind.Audio);
        SortName.Text = SortTitle(state.Sort);
        SortArrow.Data = state.Ascending ? JGIconData.ArrowUp : JGIconData.ArrowDown;
        SizeSlider.Value = state.TileSide;
        _updating = false;
        UpdateSearch();
        UpdateFlagFilter();
        UpdateBreadcrumb();
        Fit();
    }

    private void UpdateBreadcrumb()
    {
        Breadcrumb.Children.Clear();
        if (_model?.CurrentAlbum is { } album)
        {
            // An album: its name (the albums glyph before it), no folder path.
            Breadcrumb.Children.Add(new JGIcon { Data = JGIconData.Layers, Width = JGMetrics.IconSizeSmall, Height = JGMetrics.IconSizeSmall, Foreground = this.Brush(JGColor.TextTertiary) });
            var name = new TextBlock { Text = album.Name, Classes = { "body" }, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
            name.Foreground = this.Brush(JGColor.TextPrimary);
            Breadcrumb.Children.Add(name);
            return;
        }
        if (_model?.State.FolderId is not { } folderId)
        {
            return;
        }
        var path = _model.PathTo(folderId);
        for (var i = 0; i < path.Count; i++)
        {
            if (i > 0)
            {
                Breadcrumb.Children.Add(new JGIcon { Data = JGIconData.ChevronRight, Width = JGMetrics.IconSizeSmall, Height = JGMetrics.IconSizeSmall, Foreground = this.Brush(JGColor.TextTertiary) });
            }
            var folder = path[i];
            var isLast = i == path.Count - 1;
            var text = new TextBlock { Text = folder.Name, Classes = { "body" }, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
            text.Foreground = this.Brush(isLast ? JGColor.TextPrimary : JGColor.TextSecondary);
            var crumb = new Button { Content = text, Classes = { "tertiary" }, Padding = default, MinHeight = 0, Tag = folder.Id };
            crumb.Click += (_, _) => _model.SelectFolder(folder.Id);
            Breadcrumb.Children.Add(crumb);
        }
    }

    /// <summary>
    /// In a narrow window the slider steps aside first, then the kind chips become icons, then the sort menu goes
    /// (macOS ViewThatFits order). Icons also when Settings → Appearance → Kind Filters says so.
    /// </summary>
    private void Fit()
    {
        // The controls may take the bar but for the bar's padding and room for the breadcrumb.
        var available = Bounds.Width - JGInsets.Bar.Left - JGInsets.Bar.Right - JGMetrics.SearchFieldMinWidth;
        var iconsChosen = Settings.SettingsStore.Current.Settings.KindFilterStyle == Core.Settings.KindFilterStyle.Icons;
        // An album keeps its own order: no sort menu there.
        var sortAllowed = _model?.State.AlbumId is null;
        Action[] steps =
        [
            () => SizeSlider.IsVisible = false,
            () => (KindIcons.IsVisible, KindChips.IsVisible) = (true, false),
            () => SortButton.IsVisible = false,
        ];
        SizeSlider.IsVisible = _showsSizeSlider;
        (KindIcons.IsVisible, KindChips.IsVisible) = (iconsChosen, !iconsChosen);
        SortButton.IsVisible = sortAllowed;
        foreach (var step in steps)
        {
            Controls.Measure(Size.Infinity);
            if (Controls.DesiredSize.Width <= available)
            {
                return;
            }
            step();
        }
    }

    private void Toggle(MediaKind kind)
    {
        if (!_updating)
        {
            _model?.ToggleKind(kind);
        }
    }

    private void ShowSortMenu()
    {
        if (_model is null)
        {
            return;
        }
        var state = _model.State;
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        foreach (var sort in Enum.GetValues<AssetSort>())
        {
            menu.Items.Add(Radio(SortTitle(sort), "sort", state.Sort == sort, () => _model.SetSort(sort)));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Radio(L.T("Ascending"), "direction", state.Ascending, () => _model.SetAscending(true)));
        menu.Items.Add(Radio(L.T("Descending"), "direction", !state.Ascending, () => _model.SetAscending(false)));
        menu.Items.Add(new Separator());
        var subfolders = new MenuItem { Header = L.T("Include Subfolders"), ToggleType = MenuItemToggleType.CheckBox, IsChecked = state.IncludeSubfolders };
        subfolders.Click += (_, _) => _model.SetIncludeSubfolders(!state.IncludeSubfolders);
        menu.Items.Add(subfolders);
        menu.ShowAt(SortButton);
    }

    private static MenuItem Radio(string title, string group, bool isChecked, Action choose)
    {
        var item = new MenuItem { Header = title, ToggleType = MenuItemToggleType.Radio, GroupName = group, IsChecked = isChecked };
        item.Click += (_, _) => choose();
        return item;
    }

    public static string SortTitle(AssetSort sort) => sort switch
    {
        AssetSort.DateModified => L.T("Date Modified"),
        AssetSort.DateCaptured => L.T("Date Taken"),
        AssetSort.Size => L.T("Size"),
        AssetSort.Kind => L.T("Kind"),
        _ => L.T("Name"),
    };
}
