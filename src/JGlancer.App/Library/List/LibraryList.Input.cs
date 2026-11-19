using Avalonia;
using Avalonia.Input;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.Library.List;

/// <summary>
/// The list's input (D-125): a header click sorts by its column (again: the other way), a drag at a header's edge sizes
/// the column, a drag of a header moves it; rows select as in the grid (Ctrl toggles, Shift extends), a chevron or ←
/// and → fold and unfold, a double-click on a folder shows it, on a file opens it (Alt: a song in the mini player);
/// typing selects the next row whose name starts so.
/// </summary>
internal sealed partial class LibraryList
{
    private const double EdgeReach = 4;
    private static readonly TimeSpan TypingPause = TimeSpan.FromSeconds(1);

    private PointerPressedEventArgs? _dragPress;
    private Point _pressPoint;
    private long? _clickSelects;
    private (ListColumn Column, double StartWidth, double StartX)? _resizing;
    private (ListColumn Column, double X, double Width)? _movingColumn;
    private ListColumn? _pressedHeader;
    private string _typed = "";
    private DateTime _typedAt;

    /// <summary>The row under a point (control coordinates), or null.</summary>
    private int? RowAt(Point point)
    {
        var y = point.Y + _offset.Y - RowsTop;
        if (point.Y < RowsTop || y < 0)
        {
            return null;
        }
        var index = (int)(y / RowHeight);
        return index < _visible.Count ? index : null;
    }

    private bool InHeader(Point point) => point.Y >= _insets.Top && point.Y < _insets.Top + HeaderHeight;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_model is not { } model)
        {
            return;
        }
        Focus();
        var point = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;
        if (InHeader(point))
        {
            if (properties.IsLeftButtonPressed)
            {
                PressHeader(point, e);
            }
            e.Handled = true;
            return;
        }
        if (RowAt(point) is not { } index)
        {
            if (properties.IsLeftButtonPressed && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                model.Selection.Clear();
                FolderFocus = null;
                InvalidateVisual();
            }
            return;
        }
        var item = _visible[index];
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (item.Row.IsFolder)
        {
            ChooseFolder(item.Row.Id);
            if (properties.IsRightButtonPressed)
            {
                FolderMenuRequested?.Invoke(this, item.Row.Id);
            }
            else if (properties.IsLeftButtonPressed && (e.ClickCount == 2 || OnDisclosure(point, item)))
            {
                if (e.ClickCount == 2 && !OnDisclosure(point, item))
                {
                    // As the sidebar: that folder shows.
                    model.SelectFolder(item.Row.Id);
                }
                else
                {
                    Toggle(item.Row.Id);
                }
            }
            e.Handled = true;
            return;
        }
        var id = item.Row.Id;
        var selection = model.Selection;
        if (properties.IsRightButtonPressed)
        {
            if (!selection.Contains(id))
            {
                selection.Select(id);
            }
            e.Handled = true;
            ContextMenuRequested?.Invoke(this, id);
            return;
        }
        if (!properties.IsLeftButtonPressed)
        {
            return;
        }
        if (e.ClickCount == 2 && !control && !shift)
        {
            selection.Select(id);
            if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                model.PlayInMiniPlayer(id);
            }
            else
            {
                model.Open(id);
            }
        }
        else if (control)
        {
            selection.Toggle(id);
        }
        else if (shift)
        {
            selection.Extend(id, _assetOrder);
        }
        else if (selection.Contains(id) && selection.Count > 1)
        {
            _clickSelects = id;
        }
        else
        {
            selection.Select(id);
        }
        if (!control && !shift && e.ClickCount == 1)
        {
            _dragPress = e;
            _pressPoint = point;
        }
        e.Handled = true;
    }

    private bool OnDisclosure(Point point, VisibleListRow item)
    {
        var x = Columns()[0].X + item.Depth * Indent;
        return point.X >= x && point.X < x + Disclosure;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        if (_resizing is { } resize)
        {
            var width = Math.Max(ListLayout.MinColumnWidth, resize.StartWidth + point.X - resize.StartX);
            SetColumns(Saved().Select(c => c.Column == resize.Column ? c with { Width = width } : c).ToList());
            return;
        }
        if (_pressedHeader is { } pressed && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && (_movingColumn is not null || Math.Abs(point.X - _pressPoint.X) >= JGMetrics.DragThreshold))
        {
            MoveColumn(pressed, point);
            return;
        }
        if (_dragPress is { } press && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && Vector.Distance(point, _pressPoint) >= JGMetrics.DragThreshold)
        {
            _dragPress = null;
            _clickSelects = null;
            DragRequested?.Invoke(this, press);
            return;
        }
        Cursor = InHeader(point) && EdgeAt(point) is not null ? new Cursor(StandardCursorType.SizeWestEast) : null;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragPress = null;
        if (_clickSelects is { } clicked)
        {
            _clickSelects = null;
            _model?.Selection.Select(clicked);
        }
        if (_resizing is not null)
        {
            _resizing = null;
            e.Pointer.Capture(null);
            return;
        }
        if (_movingColumn is not null)
        {
            _movingColumn = null;
            _pressedHeader = null;
            e.Pointer.Capture(null);
            InvalidateVisual();
            return;
        }
        if (_pressedHeader is { } column && InHeader(e.GetPosition(this)))
        {
            Sort(ColumnAt(e.GetPosition(this)));
        }
        else if (_headerNamePressed && InHeader(e.GetPosition(this)))
        {
            Sort(null);
        }
        _pressedHeader = null;
        _headerNamePressed = false;
        e.Pointer.Capture(null);
    }

    private bool _headerNamePressed;

    private void PressHeader(Point point, PointerPressedEventArgs e)
    {
        _pressPoint = point;
        if (EdgeAt(point) is { } edge)
        {
            _resizing = (edge, Saved().First(c => c.Column == edge).Width, point.X);
            e.Pointer.Capture(this);
            return;
        }
        if (ColumnAt(point) is { } column)
        {
            _pressedHeader = column;
        }
        else
        {
            _headerNamePressed = true;
        }
        e.Pointer.Capture(this);
    }

    /// <summary>The column whose right edge is under the pointer (Name's edge sizes the first column after it).</summary>
    private ListColumn? EdgeAt(Point point)
    {
        var columns = Columns();
        for (var i = 1; i < columns.Count; i++)
        {
            var (column, x, width) = columns[i];
            if (Math.Abs(point.X - (x + width)) <= EdgeReach && column is { } found)
            {
                return found;
            }
        }
        return null;
    }

    private ListColumn? ColumnAt(Point point) =>
        Columns().Skip(1).FirstOrDefault(c => point.X >= c.X && point.X < c.X + c.Width).Column;

    /// <summary>A header click: sort by its column; the same column again turns the order around.</summary>
    private void Sort(ListColumn? column)
    {
        if (_model is not { } model)
        {
            return;
        }
        var sort = column switch
        {
            ListColumn.DateModified => AssetSort.DateModified,
            ListColumn.Size => AssetSort.Size,
            ListColumn.Kind => AssetSort.Kind,
            _ => AssetSort.Name,
        };
        if (model.State.Sort == sort)
        {
            model.SetAscending(!model.State.Ascending);
        }
        else
        {
            model.SetSort(sort);
        }
    }

    private IReadOnlyList<ListColumnWidth> Saved() => _model?.State.ListColumns ?? ListLayout.DefaultColumns;

    private void SetColumns(IReadOnlyList<ListColumnWidth> columns)
    {
        _model?.SetListColumns(columns);
        _texts.Clear();
        InvalidateVisual();
    }

    /// <summary>A header dragged sideways: it takes the place of the column under the pointer.</summary>
    private void MoveColumn(ListColumn column, Point point)
    {
        var saved = Saved().ToList();
        var columns = Columns();
        var current = columns.First(c => c.Column == column);
        _movingColumn = (column, Math.Max(columns[0].X + columns[0].Width, point.X - current.Width / 2), current.Width);
        if (ColumnAt(point) is { } target && target != column)
        {
            var moving = saved.First(c => c.Column == column);
            saved.Remove(moving);
            saved.Insert(saved.FindIndex(c => c.Column == target) + (columns.First(c => c.Column == target).X > current.X ? 1 : 0), moving);
            SetColumns(saved);
        }
        InvalidateVisual();
    }

    // MARK: Rows

    private void ChooseFolder(long folderId)
    {
        _model?.Selection.Clear();
        FolderFocus = folderId;
        InvalidateVisual();
    }

    private void Toggle(long folderId)
    {
        if (_model is not { } model)
        {
            return;
        }
        if (!model.ListExpanded.Remove(folderId))
        {
            model.ListExpanded.Add(folderId);
        }
        Reshow();
    }

    /// <summary>The visible row with the keyboard's focus: the chosen folder, or the focused file.</summary>
    private int? FocusIndex()
    {
        for (var i = 0; i < _visible.Count; i++)
        {
            var row = _visible[i].Row;
            if (row.IsFolder ? FolderFocus == row.Id : _model?.Selection.Focus == row.Id)
            {
                return i;
            }
        }
        return null;
    }

    private void Focus(int index, bool extending)
    {
        if (_model is not { } model || index < 0 || index >= _visible.Count)
        {
            return;
        }
        var row = _visible[index].Row;
        if (row.IsFolder)
        {
            ChooseFolder(row.Id);
        }
        else if (extending)
        {
            model.Selection.Extend(row.Id, _assetOrder);
        }
        else
        {
            model.Selection.Select(row.Id);
        }
        ScrollToRow(index);
    }

    /// <summary>Moves the selection as an arrow key does (Quick Preview passes its arrows here).</summary>
    public void MoveSelection(GridMove move)
    {
        var current = FocusIndex();
        var page = Math.Max(1, (int)((Bounds.Height - RowsTop - _insets.Bottom) / RowHeight));
        var target = move switch
        {
            GridMove.Up or GridMove.Left => (current ?? 1) - 1,
            GridMove.Down or GridMove.Right => current is { } at ? at + 1 : 0,
            GridMove.PageUp => (current ?? 0) - page,
            GridMove.PageDown => (current ?? 0) + page,
            GridMove.Home => 0,
            _ => _visible.Count - 1,
        };
        Focus(Math.Clamp(target, 0, Math.Max(0, _visible.Count - 1)), extending: false);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_model is not { } model || e.Handled)
        {
            return;
        }
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var current = FocusIndex();
        var page = Math.Max(1, (int)((Bounds.Height - RowsTop - _insets.Bottom) / RowHeight));
        int? target = e.Key switch
        {
            Key.Up => (current ?? 1) - 1,
            Key.Down => current is { } at ? at + 1 : 0,
            Key.PageUp => (current ?? 0) - page,
            Key.PageDown => (current ?? 0) + page,
            Key.Home => 0,
            Key.End => _visible.Count - 1,
            _ => null,
        };
        if (target is { } index && _visible.Count > 0)
        {
            Focus(Math.Clamp(index, 0, _visible.Count - 1), shift);
            e.Handled = true;
            return;
        }
        if (e.Key is Key.Right or Key.Left && current is { } focused)
        {
            var item = _visible[focused];
            if (item.Row.IsFolder && (e.Key == Key.Right) != item.IsExpanded)
            {
                Toggle(item.Row.Id);
            }
            else if (e.Key == Key.Right && item.Row.IsFolder)
            {
                Focus(focused + 1, extending: false);
            }
            else if (e.Key == Key.Left && item.Depth > 0)
            {
                // To the folder row it sits in.
                for (var i = focused - 1; i >= 0; i--)
                {
                    if (_visible[i].Depth == item.Depth - 1)
                    {
                        Focus(i, extending: false);
                        break;
                    }
                }
            }
            e.Handled = true;
            return;
        }
        if ((e.Key == Key.Apps || (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift)) && current is { } menu)
        {
            var row = _visible[menu].Row;
            if (row.IsFolder)
            {
                FolderMenuRequested?.Invoke(this, row.Id);
            }
            else
            {
                ContextMenuRequested?.Invoke(this, row.Id);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control)
        {
            model.Selection.SelectAll(_assetOrder);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && FolderFocus is { } folder)
        {
            model.SelectFolder(folder);
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.None && QuickPreviewKeyOf(e.Key) is { } key && model.OfferQuickPreviewKey(key))
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && (!model.Selection.IsEmpty || FolderFocus is not null))
        {
            model.Selection.Clear();
            FolderFocus = null;
            InvalidateVisual();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && model.Selection.Focus is { } file)
        {
            model.Open(file);
            e.Handled = true;
        }
    }

    private static QuickPreviewKey? QuickPreviewKeyOf(Key key) => key switch
    {
        Key.Space => QuickPreviewKey.Space,
        Key.Escape => QuickPreviewKey.Escape,
        Key.Enter => QuickPreviewKey.Enter,
        Key.F11 => QuickPreviewKey.FullScreen,
        _ => null,
    };

    /// <summary>Typing selects the next row whose name starts with what was typed (a pause starts over).</summary>
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || string.IsNullOrWhiteSpace(e.Text) || _visible.Count == 0)
        {
            return;
        }
        var now = DateTime.UtcNow;
        _typed = now - _typedAt > TypingPause ? e.Text : _typed + e.Text;
        _typedAt = now;
        var start = _typed.Length == 1 ? (FocusIndex() ?? -1) + 1 : FocusIndex() ?? 0;
        for (var step = 0; step < _visible.Count; step++)
        {
            var index = (start + step) % _visible.Count;
            if (RowName(_visible[index].Row).StartsWith(_typed, StringComparison.CurrentCultureIgnoreCase))
            {
                Focus(index, extending: false);
                e.Handled = true;
                return;
            }
        }
    }

    private string RowName(ListRow row) => row.IsFolder
        ? _outline.Folders.GetValueOrDefault(row.Id)?.Name ?? ""
        : _rows.GetValueOrDefault(row.Id)?.Name ?? "";

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && _model is { } model)
        {
            // Ctrl+wheel sets the row height, as the size ruler does while the list shows.
            if (e.Delta.Y != 0)
            {
                model.SetListRowHeight(model.State.ListRowHeight + Math.Sign(e.Delta.Y) * 4);
            }
            e.Handled = true;
            return;
        }
        Offset = new Vector(0, _offset.Y - e.Delta.Y * WheelStep);
        RaiseScrollInvalidated(EventArgs.Empty);
        e.Handled = true;
    }
}
