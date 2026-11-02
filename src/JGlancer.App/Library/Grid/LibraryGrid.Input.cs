using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using JGlancer.Core.Model;

namespace JGlancer.App.Library.Grid;

/// <summary>
/// Selection input (macOS GridController+Input, Explorer conventions): click, Ctrl+click toggles, Shift+click extends,
/// marquee with auto-scroll, keyboard moves with Shift extending, Ctrl+A, Esc, Enter, double-click opens.
/// </summary>
internal sealed partial class LibraryGrid
{
    private const double AutoScrollMargin = 24;
    private const double AutoScrollStep = 24;
    /// <summary>A right click on a tile (its asset id; the selection already holds it): the window shows the actions.</summary>
    public event EventHandler<long>? ContextMenuRequested;

    /// <summary>A tile was dragged: the selection's files leave as a file drag (the press starts it).</summary>
    public event EventHandler<PointerPressedEventArgs>? DragRequested;

    /// <summary>Pointer travel that turns a press on a tile into a drag.</summary>
    private const double DragThreshold = 4;

    private PointerPressedEventArgs? _dragPress;
    private Point _dragOrigin;

    /// <summary>A plain click on a tile of a multiple selection selects it alone on release, unless it became a drag.</summary>
    private long? _clickSelects;

    private int? _hoverIndex;
    private GridRect? _marquee;
    private Point _marqueeStart;
    private Point _lastPointer;
    private long? _marqueeLastHit;
    private DispatcherTimer? _autoScroll;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        _lastPointer = point;
        if (_marquee is not null)
        {
            UpdateMarquee();
            return;
        }
        if (_dragPress is { } press && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && Vector.Distance(point, _dragOrigin) >= DragThreshold)
        {
            _dragPress = null;
            _clickSelects = null;
            DragRequested?.Invoke(this, press);
            return;
        }
        var hover = _geometry.IndexAt(point.X, point.Y + _offset.Y);
        if (hover >= Refs.Count)
        {
            hover = null;
        }
        if (hover != _hoverIndex)
        {
            FadeHover(_hoverIndex, hover);
            _hoverIndex = hover;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverIndex is not null)
        {
            FadeHover(_hoverIndex, null);
            _hoverIndex = null;
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_model is null)
        {
            return;
        }
        Focus();
        var properties = e.GetCurrentPoint(this).Properties;
        var point = e.GetPosition(this);
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var selection = _model.Selection;
        if (_geometry.IndexAt(point.X, point.Y + _offset.Y) is { } index && index < Refs.Count)
        {
            var id = Refs[index].Id;
            if (properties.IsRightButtonPressed)
            {
                // Like Explorer: a right click on an unselected tile selects it first, then the menu opens.
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
            // An image sequence's badge folds or unfolds it (W-81).
            if (_sequenceBadges.TryGetValue(id, out var badge) && badge.Contains(point) && !control && !shift)
            {
                selection.Select(id);
                _model.ToggleSequence(id);
                e.Handled = true;
                return;
            }
            if (e.ClickCount == 2 && !control && !shift && _model.IsFolded(id))
            {
                // A double-click on a folded stack unfolds it (Enter still opens its first frame).
                selection.Select(id);
                _model.ToggleSequence(id);
            }
            else if (e.ClickCount == 2 && !control && !shift)
            {
                selection.Select(id);
                // Alt+double-click: a song plays in the mini player (W-68).
                if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
                {
                    _model.PlayInMiniPlayer(id);
                }
                else
                {
                    _model.Open(id);
                }
            }
            else if (control)
            {
                selection.Toggle(id);
            }
            else if (shift)
            {
                selection.Extend(id, _model.Order);
            }
            else if (selection.Contains(id) && selection.Count > 1)
            {
                // The selection may be about to be dragged: it stays until the release.
                _clickSelects = id;
            }
            else
            {
                selection.Select(id);
            }
            if (!control && !shift && e.ClickCount == 1)
            {
                _dragPress = e;
                _dragOrigin = point;
            }
            e.Handled = true;
            return;
        }
        if (properties.IsLeftButtonPressed)
        {
            _marqueeStart = new Point(point.X, point.Y + _offset.Y);
            _marquee = new GridRect(_marqueeStart.X, _marqueeStart.Y, 0, 0);
            _marqueeLastHit = null;
            selection.BeginMarquee(adding: control || shift);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
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
        if (_marquee is null)
        {
            return;
        }
        _marquee = null;
        _autoScroll?.Stop();
        e.Pointer.Capture(null);
        _model?.Selection.CommitMarquee(_marqueeLastHit);
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_marquee is not null)
        {
            _marquee = null;
            _autoScroll?.Stop();
            _model?.Selection.CommitMarquee(_marqueeLastHit);
            InvalidateVisual();
        }
    }

    private void UpdateMarquee()
    {
        if (_model is null)
        {
            return;
        }
        var current = new Point(_lastPointer.X, _lastPointer.Y + _offset.Y);
        var rect = GridRect.Between(_marqueeStart.X, _marqueeStart.Y, current.X, current.Y);
        _marquee = rect;
        var refs = Refs;
        var ids = _geometry.IndicesIntersecting(rect).Where(i => i < refs.Count).Select(i => refs[i].Id).ToList();
        if (ids.Count > 0)
        {
            _marqueeLastHit = ids[^1];
        }
        _model.Selection.Marquee(ids);
        InvalidateVisual();
        // Auto-scroll while the pointer is past the top or bottom edge (a timer only during the drag).
        var outside = _lastPointer.Y < AutoScrollMargin || _lastPointer.Y > Bounds.Height - AutoScrollMargin;
        if (outside)
        {
            _autoScroll ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) => AutoScroll());
            _autoScroll.Start();
        }
        else
        {
            _autoScroll?.Stop();
        }
    }

    private void AutoScroll()
    {
        if (_marquee is null)
        {
            _autoScroll?.Stop();
            return;
        }
        var direction = _lastPointer.Y < AutoScrollMargin ? -1 : _lastPointer.Y > Bounds.Height - AutoScrollMargin ? 1 : 0;
        if (direction == 0)
        {
            _autoScroll?.Stop();
            return;
        }
        Offset = new Vector(0, _offset.Y + direction * AutoScrollStep);
        RaiseScrollInvalidated(EventArgs.Empty);
        UpdateMarquee();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_model is null || e.Handled)
        {
            return;
        }
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        GridMove? move = e.Key switch
        {
            Key.Left => GridMove.Left,
            Key.Right => GridMove.Right,
            Key.Up => GridMove.Up,
            Key.Down => GridMove.Down,
            Key.PageUp => GridMove.PageUp,
            Key.PageDown => GridMove.PageDown,
            Key.Home => GridMove.Home,
            Key.End => GridMove.End,
            _ => null,
        };
        if (move is { } direction)
        {
            Move(direction, shift);
            e.Handled = true;
        }
        else if ((e.Key == Key.Apps || (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift)) && _model.Selection.Focus is { } menuFocus)
        {
            // The menu key or Shift+F10: the selection's actions, as a right click.
            ContextMenuRequested?.Invoke(this, menuFocus);
            e.Handled = true;
        }
        // F2 and Delete (Explorer's keys, W-45) are the window's shortcuts now: Settings → Shortcuts can change them.
        else if (e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control)
        {
            _model.Selection.SelectAll(_model.Order);
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.None && QuickPreviewKeyOf(e.Key) is { } key && _model.OfferQuickPreviewKey(key))
        {
            // Space toggles Quick Preview; Esc, Enter and F11 go to it while it is open.
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && !_model.Selection.IsEmpty)
        {
            _model.Selection.Clear();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && _model.Selection.Focus is { } focus)
        {
            _model.Open(focus);
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

    /// <summary>Moves the selection like an arrow key in the grid (Quick Preview passes its arrows here).</summary>
    public void MoveSelection(GridMove move) => Move(move, extending: false);

    private void Move(GridMove move, bool extending)
    {
        if (_model is null || _geometry.ItemCount == 0)
        {
            return;
        }
        var current = _model.Selection.Focus is { } focus ? _model.Order.IndexOf(focus) : null;
        var target = current is { } index
            ? _geometry.Move(index, move, _geometry.RowsPerPage(Bounds.Height))
            : move == GridMove.End ? _geometry.ItemCount - 1 : 0;
        var id = Refs[target].Id;
        if (extending)
        {
            _model.Selection.Extend(id, _model.Order);
        }
        else
        {
            _model.Selection.Select(id);
        }
        ScrollToVisible(target);
    }
}
