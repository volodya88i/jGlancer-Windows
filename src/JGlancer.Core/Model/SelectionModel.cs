namespace JGlancer.Core.Model;

/// <summary>The ordered ids currently shown, with O(1) position lookup.</summary>
public sealed class OrderedIds
{
    private readonly Dictionary<long, int> _positions;

    public OrderedIds(IReadOnlyList<long> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        Ids = ids;
        _positions = new Dictionary<long, int>(ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            _positions[ids[i]] = i;
        }
    }

    public static OrderedIds Empty { get; } = new([]);

    public IReadOnlyList<long> Ids { get; }

    public int? IndexOf(long id) => _positions.TryGetValue(id, out var index) ? index : null;

    public bool Contains(long id) => _positions.ContainsKey(id);
}

/// <summary>
/// Selection in the library grid, by asset id so it survives re-sorting and data changes (macOS SelectionModel).
/// Explorer semantics: click selects, Ctrl+click toggles, Shift+click and Shift+arrows extend a range from the
/// anchor, marquee replaces or adds.
/// </summary>
public sealed class SelectionModel
{
    private HashSet<long> _selected = [];
    // Selection that range extensions and marquees add to.
    private HashSet<long> _base = [];

    /// <summary>Raised after every change.</summary>
    public event EventHandler? Changed;

    public IReadOnlySet<long> Selected => _selected;

    /// <summary>Start of range extensions.</summary>
    public long? Anchor { get; private set; }

    /// <summary>The keyboard cursor (last clicked or moved-to item).</summary>
    public long? Focus { get; private set; }

    public int Count => _selected.Count;

    public bool IsEmpty => _selected.Count == 0;

    public bool Contains(long id) => _selected.Contains(id);

    /// <summary>
    /// The file the previews show (macOS PreviewModel.selectionChanged): the focus while it is selected, else the only
    /// selected file; null otherwise.
    /// </summary>
    public long? Previewed => Focus is { } focus && _selected.Contains(focus) ? focus
        : _selected.Count == 1 ? _selected.First() : null;

    /// <summary>Plain click or arrow: only <paramref name="id"/>.</summary>
    public void Select(long id)
    {
        _selected = [id];
        _base = [];
        Anchor = id;
        Focus = id;
        Notify();
    }

    /// <summary>Ctrl+click: adds or removes <paramref name="id"/>.</summary>
    public void Toggle(long id)
    {
        if (!_selected.Remove(id))
        {
            _selected.Add(id);
        }
        _base = [.. _selected];
        Anchor = id;
        Focus = id;
        Notify();
    }

    /// <summary>Shift+click or Shift+arrow: the range from the anchor, added to the selection before the extension started.</summary>
    public void Extend(long id, OrderedIds order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (Anchor is not { } anchor || order.IndexOf(anchor) is not { } start || order.IndexOf(id) is not { } end)
        {
            Select(id);
            return;
        }
        var range = new HashSet<long>(_base);
        for (var i = Math.Min(start, end); i <= Math.Max(start, end); i++)
        {
            range.Add(order.Ids[i]);
        }
        _selected = range;
        Focus = id;
        Notify();
    }

    /// <summary>Starts a marquee drag; <paramref name="adding"/> (Ctrl or Shift held) keeps the current selection.</summary>
    public void BeginMarquee(bool adding)
    {
        _base = adding ? [.. _selected] : [];
        if (!adding)
        {
            _selected = [];
        }
        Notify();
    }

    /// <summary>Updates a marquee drag to cover <paramref name="ids"/>.</summary>
    public void Marquee(IEnumerable<long> ids)
    {
        var next = new HashSet<long>(_base);
        next.UnionWith(ids);
        if (next.SetEquals(_selected))
        {
            return;
        }
        _selected = next;
        Notify();
    }

    /// <summary>Ends a marquee drag so the next one starts from the current selection.</summary>
    public void CommitMarquee(long? focus)
    {
        _base = [.. _selected];
        if (focus is { } id)
        {
            Anchor = id;
            Focus = id;
        }
        Notify();
    }

    /// <summary>Ctrl+A.</summary>
    public void SelectAll(OrderedIds order)
    {
        ArgumentNullException.ThrowIfNull(order);
        _selected = [.. order.Ids];
        _base = [.. _selected];
        Anchor = order.Ids.Count > 0 ? order.Ids[0] : null;
        Focus = order.Ids.Count > 0 ? order.Ids[^1] : null;
        Notify();
    }

    public void Clear()
    {
        if (_selected.Count == 0 && Anchor is null && Focus is null)
        {
            return;
        }
        _selected = [];
        _base = [];
        Anchor = null;
        Focus = null;
        Notify();
    }

    /// <summary>Restores a saved selection.</summary>
    public void Restore(IEnumerable<long> ids, long? focus)
    {
        _selected = [.. ids];
        _base = [.. _selected];
        Anchor = focus;
        Focus = focus;
        Notify();
    }

    /// <summary>Drops ids that are no longer shown (after filtering, deletion or a folder change).</summary>
    public void Retain(OrderedIds order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var before = _selected.Count;
        _selected.RemoveWhere(id => !order.Contains(id));
        _base.RemoveWhere(id => !order.Contains(id));
        var changed = before != _selected.Count;
        if (Anchor is { } anchor && !order.Contains(anchor))
        {
            Anchor = null;
            changed = true;
        }
        if (Focus is { } focus && !order.Contains(focus))
        {
            Focus = null;
            changed = true;
        }
        if (changed)
        {
            Notify();
        }
    }

    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
}
