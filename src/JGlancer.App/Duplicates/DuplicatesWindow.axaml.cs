using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using JGlancer.App.FileOps;
using JGlancer.App.Services;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Design;
using JGlancer.Media.Thumbnails;

namespace JGlancer.App.Duplicates;

/// <summary>
/// The Duplicates window (W-78): groups of copies, each copy with its thumbnail, name, folder and date and a box that
/// sends it to the Recycle Bin when ticked; a double-click shows the copy in the library. "Move N to Recycle Bin"
/// proves every ticked copy against one that stays, byte for byte, first. Esc closes.
/// </summary>
internal sealed partial class DuplicatesWindow : Window
{
    private readonly DuplicatesModel _model;
    private readonly AppServices _services;
    private readonly FileOperationsModel _fileOps;
    private readonly List<Bitmap> _bitmaps = [];
    private bool _removing;

    // The XAML loader needs a parameterless constructor; the app always passes the model.
    public DuplicatesWindow()
        : this(null!, null!)
    {
    }

    public DuplicatesWindow(AppServices services, FileOperationsModel fileOps)
    {
        InitializeComponent();
        _services = services;
        _fileOps = fileOps;
        _model = new DuplicatesModel(services);
        GroupList.ItemTemplate = new FuncDataTemplate<DuplicateGroup>((group, _) => GroupCard(group), supportsRecycling: false);
        _model.Changed += (_, _) => Update();
        CloseButton.Click += (_, _) => Close();
        RemoveButton.Click += async (_, _) => await RemoveAsync();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
        if (services is not null)
        {
            Opened += async (_, _) => await _model.LoadAsync();
        }
        Closed += (_, _) =>
        {
            foreach (var bitmap in _bitmaps)
            {
                bitmap.Dispose();
            }
            _bitmaps.Clear();
        };
    }

    /// <summary>A copy was double-clicked: the library shows it (its library id).</summary>
    public event EventHandler<long>? RevealRequested;

    private void Update()
    {
        var groups = _model.Groups;
        var extra = groups.Sum(g => g.Copies.Count - 1);
        var pending = _model.Pending > 0 ? " " + L.T("{0} files are still being read; more may turn up.", _model.Pending.ToString("N0", CultureInfo.CurrentCulture)) : "";
        SummaryText.Text = _model.IsLoading
            ? L.T("Looking for duplicates…")
            : groups.Count == 0
                ? L.T("No duplicates found.") + pending
                : L.Plural("{0} groups", groups.Count) + " · " + L.Plural("{0} extra copies", extra) + " · "
                    + ByteFormat.Format(groups.Sum(g => (g.Copies.Count - 1) * g.Size)) + pending;
        EmptyText.IsVisible = !_model.IsLoading && groups.Count == 0;
        EmptyText.Text = L.T("No duplicates found.");
        if (!ReferenceEquals(GroupList.ItemsSource, groups))
        {
            GroupList.ItemsSource = groups;
        }
        var count = _model.ToRemoveCount;
        RemoveButton.Content = L.T("Move {0} to Recycle Bin", count.ToString("N0", CultureInfo.CurrentCulture));
        RemoveButton.IsEnabled = count > 0 && !_removing;
        FreedText.Text = count > 0 ? L.T("Frees {0}", ByteFormat.Format(_model.ToRemoveBytes)) : "";
    }

    private async Task RemoveAsync()
    {
        if (_removing || _model.ToRemoveCount == 0)
        {
            return;
        }
        _removing = true;
        RemoveButton.IsEnabled = false;
        SummaryText.Text = L.T("Comparing the copies byte for byte…");
        try
        {
            var (_, different) = await _model.RemoveAsync(_fileOps);
            if (different > 0)
            {
                _fileOps.ShowMessage(L.T("Copies that turned out to differ were left alone: {0}", different.ToString(CultureInfo.CurrentCulture)));
            }
        }
        finally
        {
            _removing = false;
        }
        // The library forgets the removed files as the watcher reports them; look again then.
        await Task.Delay(TimeSpan.FromSeconds(1));
        await _model.LoadAsync();
    }

    private Border GroupCard(DuplicateGroup group)
    {
        var header = new TextBlock
        {
            Text = L.Plural("{0} copies", group.Copies.Count) + " · " + ByteFormat.Format(group.Size),
            Classes = { "label" },
        };
        header[!TextBlock.ForegroundProperty] = this.GetResourceObservable(JGColor.TextTertiary.ResourceKey).ToBinding();
        var rows = new StackPanel { Spacing = JGSpacing.Xxs };
        rows.Children.Add(header);
        foreach (var copy in group.Copies)
        {
            rows.Children.Add(CopyRow(group, copy));
        }
        var card = new Border
        {
            Child = rows,
            Padding = new Thickness(JGSpacing.S),
            Margin = new Thickness(0, 0, 0, JGSpacing.S),
            CornerRadius = new CornerRadius(JGMetrics.RadiusMedium),
            BorderThickness = new Thickness(JGMetrics.OutlineWidth),
        };
        card[!Border.BackgroundProperty] = this.GetResourceObservable(JGColor.BgRaised.ResourceKey).ToBinding();
        card[!Border.BorderBrushProperty] = this.GetResourceObservable(JGColor.LineHair.ResourceKey).ToBinding();
        return card;
    }

    private Avalonia.Controls.Grid CopyRow(DuplicateGroup group, Asset copy)
    {
        var box = new CheckBox { IsChecked = group.ToRemove.Contains(copy.Id), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(box, L.T("Move this copy to the Recycle Bin"));
        AutomationProperties.SetName(box, L.T("Move this copy to the Recycle Bin") + ": " + copy.Name);
        box.IsCheckedChanged += (_, _) =>
        {
            _model.SetRemoved(group, copy.Id, box.IsChecked == true);
            // One copy always stays: a box that could not be ticked goes back.
            box.IsChecked = group.ToRemove.Contains(copy.Id);
        };
        var side = JGMetrics.NowPlayingCoverSide;
        var picture = new Support.SafeImage { Width = side, Height = side, Stretch = Stretch.UniformToFill };
        var frame = new Border { Width = side, Height = side, CornerRadius = new CornerRadius(JGMetrics.RadiusSmall), ClipToBounds = true, Child = picture };
        frame[!Border.BackgroundProperty] = this.GetResourceObservable(JGColor.BgBase.ResourceKey).ToBinding();
        _ = LoadThumbnailAsync(copy, picture);
        var name = new TextBlock { Text = copy.Name, Classes = { "body" }, TextTrimming = TextTrimming.CharacterEllipsis };
        name[!TextBlock.ForegroundProperty] = this.GetResourceObservable(JGColor.TextPrimary.ResourceKey).ToBinding();
        var folder = new TextBlock { Text = Path.GetDirectoryName(copy.Path), Classes = { "body" }, TextTrimming = TextTrimming.PrefixCharacterEllipsis };
        folder[!TextBlock.ForegroundProperty] = this.GetResourceObservable(JGColor.TextTertiary.ResourceKey).ToBinding();
        var local = copy.ModifiedAt.ToLocalTime();
        var date = new TextBlock
        {
            Text = local.ToString("d", CultureInfo.CurrentCulture) + " " + local.ToString("t", CultureInfo.CurrentCulture),
            Classes = { "mono" },
            VerticalAlignment = VerticalAlignment.Center,
        };
        date[!TextBlock.ForegroundProperty] = this.GetResourceObservable(JGColor.TextTertiary.ResourceKey).ToBinding();
        var grid = new Avalonia.Controls.Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = JGSpacing.S };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, folder } };
        Avalonia.Controls.Grid.SetColumn(frame, 1);
        Avalonia.Controls.Grid.SetColumn(text, 2);
        Avalonia.Controls.Grid.SetColumn(date, 3);
        grid.Children.Add(box);
        grid.Children.Add(frame);
        grid.Children.Add(text);
        grid.Children.Add(date);
        // A double-click on the copy (not on its box) shows it in the library.
        grid.Background = Brushes.Transparent;
        grid.DoubleTapped += (_, e) =>
        {
            if (e.Source is not CheckBox)
            {
                RevealRequested?.Invoke(this, copy.Id);
            }
        };
        ToolTip.SetTip(text, copy.Path);
        return grid;
    }

    private async Task LoadThumbnailAsync(Asset asset, Support.SafeImage target)
    {
        // The window's own bitmaps (the shared cache may let go of its own while they are shown), from the disk cache.
        var image = await _services.Thumbnails.GetAsync(ThumbnailRequest.For(asset, ThumbnailBucket.Small), ThumbnailPriority.Visible, CancellationToken.None);
        if (image is null || !IsVisible)
        {
            return;
        }
        var bitmap = await Task.Run(() => Library.ThumbnailCache.ToBitmap(image));
        _bitmaps.Add(bitmap);
        target.Source = bitmap;
    }
}
