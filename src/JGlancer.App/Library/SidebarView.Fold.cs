using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using JGlancer.Core.Localization;
using JGlancer.Design;

namespace JGlancer.App.Library;

/// <summary>What the sidebar's fold button does: fold the docked sidebar, or dock the one lying over the files.</summary>
internal enum SidebarFoldRole
{
    Hide,
    KeepOpen,
}

/// <summary>
/// The sidebar as one panel that folds into its strip (macOS D-128, MAC-051): the content is laid out at the unfolded
/// width all the time and LibraryView animates only the visible width. Here, at every frame of that, the names fade,
/// each strip icon (library folders, album covers, the Trash) glides between its place in its row and the strip's
/// middle, and the selection shrinks from its row to a square round the icon. The strip has no subfolders and no pinned
/// folders; rows that come or go move the strip icons up or down smoothly (where they were is kept across the rebuild).
/// </summary>
internal sealed partial class SidebarView
{
    private readonly Dictionary<string, double> _flipFrom = [];
    private readonly Dictionary<string, double> _flipOffset = [];
    private bool _morphDirty = true;
    private (double Openness, double Labels, double Strip, double Flip) _morph = (1, 1, 0, 1);
    private int _openMenus;

    /// <summary>The strip: icons only, no subfolders or pinned folders.</summary>
    public bool IsCompact { get; private set; }

    /// <summary>The fold button: Hide Sidebar or Keep Sidebar Open.</summary>
    public SidebarFoldRole FoldRole
    {
        get;
        set
        {
            field = value;
            var label = value == SidebarFoldRole.Hide ? L.T("Hide Sidebar") : L.T("Keep Sidebar Open");
            ToolTip.SetTip(FoldButton, label);
            AutomationProperties.SetName(FoldButton, label);
        }
    }

    /// <summary>The fold button (Hide or Keep Open) was clicked.</summary>
    public event EventHandler? FoldClicked;

    /// <summary>The strip's Show Sidebar was clicked.</summary>
    public event EventHandler? ShowClicked;

    /// <summary>A menu of a row closed (the sidebar over the files may go now).</summary>
    public event EventHandler? MenuClosed;

    /// <summary>A row's menu is open, or an album's name is being edited: the sidebar over the files stays.</summary>
    public bool IsHeld => _openMenus > 0 || _albumField is not null;

    private void AttachFold()
    {
        FoldRole = SidebarFoldRole.Hide;
        FoldButton.Click += (_, _) => FoldClicked?.Invoke(this, EventArgs.Empty);
        ShowButton.Click += (_, _) => ShowClicked?.Invoke(this, EventArgs.Empty);
        ToolTip.SetTip(ShowButton, L.T("Show Sidebar"));
        AutomationProperties.SetName(ShowButton, L.T("Show Sidebar"));
    }

    private void OnMenuOpened(object? sender, RoutedEventArgs e) => _openMenus++;

    private void OnMenuClosed(object? sender, RoutedEventArgs e)
    {
        _openMenus = Math.Max(0, _openMenus - 1);
        MenuClosed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Counts a menu built in code (albums, pins) like the rows' own.</summary>
    private ContextMenu Tracked(ContextMenu menu)
    {
        menu.Opened += OnMenuOpened;
        menu.Closed += OnMenuClosed;
        return menu;
    }

    /// <summary>Folds into the strip's rows or back; where the strip icons were is kept so they move on smoothly.</summary>
    public void SetCompact(bool compact)
    {
        if (IsCompact == compact)
        {
            return;
        }
        _flipFrom.Clear();
        foreach (var icon in StripIcons())
        {
            if (icon.Tag is string key)
            {
                _flipFrom[key] = Natural(icon, this).Y + (icon.RenderTransform is TranslateTransform shift ? shift.Y : 0);
            }
        }
        IsCompact = compact;
        Rebuild();
    }

    /// <summary>
    /// One frame of the fold. <paramref name="openness"/>: 0 the strip, 1 unfolded (a spring may overshoot);
    /// <paramref name="labels"/> and <paramref name="strip"/>: the opacity of the names and of what only the strip shows;
    /// <paramref name="flip"/>: how far the strip icons have come from where they were before rows came or went (0 → 1).
    /// </summary>
    public void ApplyMorph(double openness, double labels, double strip, double flip)
    {
        _morph = (openness, labels, strip, flip);
        if (_morphDirty)
        {
            // Rows were rebuilt: their places are known after the next arrange, which applies the frame.
            InvalidateArrange();
            return;
        }
        Apply();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        if (_morphDirty)
        {
            _morphDirty = false;
            _flipOffset.Clear();
            foreach (var icon in StripIcons())
            {
                if (icon.Tag is string key && _flipFrom.TryGetValue(key, out var from))
                {
                    _flipOffset[key] = from - Natural(icon, this).Y;
                }
            }
            Apply();
        }
        return arranged;
    }

    private void Apply()
    {
        var (openness, labels, strip, flip) = _morph;
        var folding = IsCompact || openness < 0.999;
        Classes.Set("folding", folding);
        foreach (var visual in this.GetVisualDescendants().OfType<Control>())
        {
            if (visual.Classes.Contains("fade"))
            {
                visual.Opacity = labels;
                visual.IsHitTestVisible = !IsCompact;
            }
            else if (visual.Classes.Contains("stripOnly"))
            {
                visual.Opacity = strip;
                visual.IsHitTestVisible = IsCompact;
            }
        }
        foreach (var icon in StripIcons())
        {
            var natural = Natural(icon, this);
            var dx = (JGMetrics.SidebarStripWidth / 2 - (natural.X + icon.Bounds.Width / 2)) * (1 - openness);
            var dy = icon.Tag is string key && _flipOffset.TryGetValue(key, out var offset) ? offset * (1 - Math.Clamp(flip, 0, 1)) : 0;
            icon.RenderTransform = dx == 0 && dy == 0 ? null : new TranslateTransform(dx, dy);
        }
        AlbumsLine.Margin = new Thickness(JGMetrics.SidebarStripWidth / 2 - AlbumsLine.Width / 2 - Natural(AlbumsSection, this).X, 0, 0, 0);
        PlaceSelectionMark(folding, openness, flip);
    }

    /// <summary>The selected row's highlight while folded or folding: its row, or a square round its icon on the strip.</summary>
    private void PlaceSelectionMark(bool folding, double openness, double flip)
    {
        var row = folding ? SelectedRow() : null;
        if (row is null || row.Bounds.Height <= 0)
        {
            SelectionMark.IsVisible = false;
            return;
        }
        var rowAt = Natural(row, MarkLayer.GetVisualParent()!);
        var icon = row.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Classes.Contains("stripIcon"));
        var dy = icon?.Tag is string key && _flipOffset.TryGetValue(key, out var offset) ? offset * (1 - Math.Clamp(flip, 0, 1)) : 0;
        var side = JGMetrics.ControlHeightCompact;
        var stripX = JGMetrics.SidebarStripWidth / 2 - side / 2 - Natural(MarkLayer, this).X;
        var t = Math.Clamp(openness, 0, 1);
        Canvas.SetLeft(SelectionMark, stripX + (rowAt.X - stripX) * t);
        Canvas.SetTop(SelectionMark, rowAt.Y + dy);
        SelectionMark.Width = side + (row.Bounds.Width - side) * t;
        SelectionMark.Height = row.Bounds.Height;
        SelectionMark.IsVisible = true;
    }

    /// <summary>The selected folder or album row on the strip (a library folder or an album), if any.</summary>
    private Button? SelectedRow() =>
        RowsPanel.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Classes.Contains("row") && b.Classes.Contains("selected")
                && b.GetVisualDescendants().OfType<Control>().Any(c => c.Classes.Contains("stripIcon")));

    private IEnumerable<Control> StripIcons() =>
        this.GetVisualDescendants().OfType<Control>().Where(c => c.Classes.Contains("stripIcon") && c.IsEffectivelyVisible);

    /// <summary>Where <paramref name="visual"/> is laid out in <paramref name="ancestor"/>, without render transforms.</summary>
    private static Point Natural(Visual visual, Visual ancestor)
    {
        double x = 0, y = 0;
        for (Visual? at = visual; at is not null && at != ancestor; at = at.GetVisualParent())
        {
            x += at.Bounds.X;
            y += at.Bounds.Y;
        }
        return new Point(x, y);
    }
}
