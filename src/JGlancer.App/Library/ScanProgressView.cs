using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using JGlancer.Core.Localization;
using JGlancer.Design;

namespace JGlancer.App.Library;

/// <summary>
/// The card at the bottom of the window while folders are being added (W-70): per folder its name, the
/// media files and folders found so far, a running bar and Stop (which takes the folder out of the library again).
/// The app stays usable meanwhile; the folder shows in the sidebar once its first scan ended.
/// </summary>
internal sealed class ScanProgressView : Border
{
    private readonly StackPanel _rows = new() { Spacing = JGSpacing.S, Margin = new Thickness(JGSpacing.M, JGSpacing.S) };
    private readonly Dictionary<long, (Control Row, TextBlock Counts)> _shown = [];
    private LibraryModel? _model;

    public ScanProgressView()
    {
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(JGSpacing.M, 0, JGSpacing.M, JGSpacing.L + JGMetrics.FooterHeight);
        MinWidth = JGMetrics.DialogWidth;
        MaxWidth = JGMetrics.ReadableTextWidth;
        CornerRadius = new CornerRadius(JGMetrics.RadiusLarge);
        BorderThickness = new Thickness(JGMetrics.OutlineWidth);
        ClipToBounds = true;
        BoxShadow = new BoxShadows(new BoxShadow { Blur = JGMetrics.ToastShadowRadius, Color = Color.FromArgb((byte)(255 * JGMetrics.ToastShadowOpacity), 0, 0, 0) });
        this[!BackgroundProperty] = this.GetResourceObservable(JGColor.BgRaised.ResourceKey).ToBinding();
        this[!BorderBrushProperty] = this.GetResourceObservable(JGColor.LineStrong.ResourceKey).ToBinding();
        Child = _rows;
        Support.WindowGlass.Frost(this, JGColor.BgRaised);
        IsVisible = false;
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
    }

    /// <summary>The main library model (its indexer runs the scans).</summary>
    public LibraryModel? Model
    {
        get => _model;
        set
        {
            if (_model is not null)
            {
                _model.Indexer.Changed -= OnIndexerChanged;
            }
            _model = value;
            if (_model is not null)
            {
                _model.Indexer.Changed += OnIndexerChanged;
            }
            Update();
        }
    }

    private void OnIndexerChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        var scans = _model?.Indexer.FirstScans;
        scans ??= new Dictionary<long, LibraryIndexer.FirstScan>();
        foreach (var gone in _shown.Keys.Where(id => !scans.ContainsKey(id)).ToList())
        {
            _rows.Children.Remove(_shown[gone].Row);
            _shown.Remove(gone);
        }
        // Rows stay (a click on Stop is never lost to a rebuild); only the counts change, four times a second at most.
        foreach (var (rootId, scan) in scans)
        {
            if (!_shown.TryGetValue(rootId, out var shown))
            {
                shown = Row(rootId, scan);
                _shown[rootId] = shown;
                _rows.Children.Add(shown.Row);
            }
            shown.Counts.Text = L.T("Media files: {0} · folders: {1}",
                scan.Progress.Files.ToString("N0", CultureInfo.CurrentCulture), scan.Progress.Directories.ToString("N0", CultureInfo.CurrentCulture));
        }
        IsVisible = _shown.Count > 0;
    }

    private (Control Row, TextBlock Counts) Row(long rootId, LibraryIndexer.FirstScan scan)
    {
        var name = Path.GetFileName(scan.Path.TrimEnd('\\', '/')) is { Length: > 0 } leaf ? leaf : scan.Path;
        var title = new TextBlock { Text = L.T("Adding “{0}”…", name), Classes = { "body" }, TextTrimming = TextTrimming.CharacterEllipsis };
        title[!TextBlock.ForegroundProperty] = this.GetResourceObservable(JGColor.TextPrimary.ResourceKey).ToBinding();
        ToolTip.SetTip(title, scan.Path);
        var counts = new TextBlock { Classes = { "mono" } };
        counts[!TextBlock.ForegroundProperty] = this.GetResourceObservable(JGColor.TextSecondary.ResourceKey).ToBinding();
        var stop = new Button { Classes = { "tertiary" }, Content = L.T("Stop"), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(stop, L.T("Stop Adding"));
        ToolTip.SetTip(stop, L.T("Stop Adding"));
        stop.Click += async (_, _) =>
        {
            if (_model is { } model)
            {
                await model.RemoveRootAsync(rootId);
            }
        };
        var bar = new ProgressBar { IsIndeterminate = true, Height = JGMetrics.OutlineWidth * 2, MinHeight = 0 };
        bar[!ProgressBar.ForegroundProperty] = this.GetResourceObservable(JGColor.Accent.ResourceKey).ToBinding();
        bar[!ProgressBar.BackgroundProperty] = this.GetResourceObservable(JGColor.LineHair.ResourceKey).ToBinding();
        var text = new StackPanel { Spacing = JGSpacing.Xxs, Children = { title, counts } };
        var grid = new Avalonia.Controls.Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = JGSpacing.S };
        grid.Children.Add(text);
        Avalonia.Controls.Grid.SetColumn(stop, 1);
        grid.Children.Add(stop);
        AutomationProperties.SetName(this, title.Text);
        return (new StackPanel { Spacing = JGSpacing.Xs, Children = { grid, bar } }, counts);
    }
}
