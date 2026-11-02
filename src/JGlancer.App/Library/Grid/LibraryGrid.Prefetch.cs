using System.Diagnostics;
using Avalonia.Threading;
using JGlancer.Core.Model;
using JGlancer.Media.Thumbnails;

namespace JGlancer.App.Library.Grid;

/// <summary>
/// Which thumbnails to make, and in what order (W-38):
/// <list type="bullet">
/// <item>While the user flings (faster than a screen a second) the grid predicts where the screen will be
/// <see cref="LeadSeconds"/> ahead and loads that screen first; tiles that only fly by are not loaded.</item>
/// <item>Tiles that leave the screen are not thrown away: they wait at background priority and load when nothing else
/// is wanted (at most <see cref="WaitingScreens"/> screens of them, the farthest dropped first).</item>
/// <item>Once scrolling stops (<see cref="SettleMilliseconds"/> without movement) the screen loads from its centre
/// outwards, then half a screen around it, then the waiting tiles.</item>
/// </list>
/// Rows (names, durations) are always fetched for what is shown; they come from the database and are cheap.
/// </summary>
internal sealed partial class LibraryGrid
{
    private const double LeadSeconds = 0.4;
    private const double FlingScreensPerSecond = 1;
    private const int SettleMilliseconds = 120;
    private const double WaitingScreens = 3;

    private double _velocity;
    private long _lastScroll;
    private DispatcherTimer? _settle;

    /// <summary>What the prefetch is doing (benchmarks): requests per priority, fling state, rows in flight.</summary>
    public string PrefetchState
    {
        get
        {
            var counts = _thumbnailTasks.Values.GroupBy(t => t.Priority).ToDictionary(g => g.Key, g => g.Count());
            var (start, end) = VisibleRange();
            var missingRows = 0;
            for (var i = start; i < end && i < Refs.Count; i++)
            {
                if (!_rows.ContainsKey(Refs[i].Id))
                {
                    missingRows++;
                }
            }
            return string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"visible={counts.GetValueOrDefault(ThumbnailPriority.Visible)} nearby={counts.GetValueOrDefault(ThumbnailPriority.Nearby)} waiting={counts.GetValueOrDefault(ThumbnailPriority.Background)} flinging={IsFlinging} velocity={_velocity:0} rowsInFlight={_rowsInFlight.Count} visibleWithoutRow={missingRows} shown={end - start}");
        }
    }

    private bool _repairPosted;

    /// <summary>
    /// A drawn tile that has neither a bitmap nor a request (its thumbnail was pushed out of memory before the screen
    /// redrew) asks again, once per frame at most.
    /// </summary>
    private void RepairMissing(Asset asset)
    {
        if (_repairPosted || IsFlinging || _thumbnailTasks.ContainsKey(asset.Id))
        {
            return;
        }
        _repairPosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            _repairPosted = false;
            UpdateWindow();
        }, DispatcherPriority.Background);
    }

    /// <summary>Scrolling faster than a screen a second.</summary>
    private bool IsFlinging => Math.Abs(_velocity) > Bounds.Height * FlingScreensPerSecond;

    /// <summary>Measures the scroll speed (pixels a second, smoothed) and restarts the wait for the stop.</summary>
    private void TrackScroll(double previous)
    {
        var now = Stopwatch.GetTimestamp();
        var seconds = _lastScroll == 0 ? double.MaxValue : Stopwatch.GetElapsedTime(_lastScroll, now).TotalSeconds;
        // A lone jump (scroll-bar click, a key) is not a fling.
        _velocity = seconds is > 0 and < 0.25 ? _velocity * 0.5 + (_offset.Y - previous) / seconds * 0.5 : 0;
        _lastScroll = now;
        _settle ??= new DispatcherTimer(TimeSpan.FromMilliseconds(SettleMilliseconds), DispatcherPriority.Background, (_, _) => Settled());
        _settle.Stop();
        _settle.Start();
    }

    private void Settled()
    {
        _settle?.Stop();
        _velocity = 0;
        _lastScroll = 0;
        UpdateWindow();
    }

    /// <summary>Asks for what should load now, demotes what scrolled away to the waiting queue, trims the queue.</summary>
    private void UpdateWindow()
    {
        if (_model is null || _geometry.ItemCount == 0 || Bounds.Height <= 0)
        {
            CancelAllThumbnails();
            return;
        }
        var refs = Refs;
        var (visibleStart, visibleEnd) = VisibleRange();
        var flinging = IsFlinging;
        var focusY = _offset.Y;
        (int Start, int End) wanted;
        if (flinging)
        {
            var maxOffset = Math.Max(0, Extent.Height - Bounds.Height);
            focusY = Math.Clamp(_offset.Y + _velocity * LeadSeconds, 0, maxOffset);
            wanted = _geometry.Indices(new GridRect(0, focusY, Bounds.Width, Bounds.Height));
        }
        else
        {
            wanted = WindowRange();
        }
        wanted = (wanted.Start, Math.Min(wanted.End, refs.Count));
        visibleEnd = Math.Min(visibleEnd, refs.Count);

        FetchMissingRows(refs, visibleStart, visibleEnd, wanted);
        // Room for the screen and the prefetch around it: at 3200 × 1200 one screen of large thumbnails is over 100 MB.
        var tileBytes = (long)_bucket.MaxPixelSize() * _bucket.MaxPixelSize() * 3;
        Thumbnails.EnsureCapacity((long)(Math.Max(1, visibleEnd - visibleStart) * tileBytes * (1 + 2 * PrefetchScreens) * 1.25));

        var active = new HashSet<long>();
        for (var i = wanted.Start; i < wanted.End; i++)
        {
            active.Add(refs[i].Id);
        }
        if (!flinging)
        {
            for (var i = visibleStart; i < visibleEnd; i++)
            {
                active.Add(refs[i].Id);
            }
        }
        Demote(active);
        TrimWaiting(visibleEnd - visibleStart);

        // The screen that matters first: the one shown once stopped, the predicted one while flinging; centre out.
        var screen = flinging ? wanted : (visibleStart, visibleEnd);
        foreach (var index in CentreOut(screen, focusY))
        {
            if (_rows.TryGetValue(refs[index].Id, out var asset))
            {
                RequestThumbnail(asset, ThumbnailPriority.Visible);
            }
        }
        if (!flinging)
        {
            for (var i = wanted.Start; i < wanted.End; i++)
            {
                if ((i < visibleStart || i >= visibleEnd) && _rows.TryGetValue(refs[i].Id, out var asset))
                {
                    RequestThumbnail(asset, ThumbnailPriority.Nearby);
                }
            }
        }
    }

    private void FetchMissingRows(IReadOnlyList<AssetRef> refs, int visibleStart, int visibleEnd, (int Start, int End) wanted)
    {
        var missing = new List<long>();
        void Add(int start, int end)
        {
            for (var i = start; i < end; i++)
            {
                var id = refs[i].Id;
                if (!_rows.ContainsKey(id) && !_rowsInFlight.Contains(id))
                {
                    missing.Add(id);
                }
            }
        }
        Add(visibleStart, visibleEnd);
        Add(wanted.Start, wanted.End);
        if (missing.Count > 0)
        {
            FetchRows([.. missing.Distinct()], replace: false);
        }
    }

    /// <summary>Indices of <paramref name="range"/> by distance of their tile from the middle of the screen at <paramref name="top"/>.</summary>
    private IEnumerable<int> CentreOut((int Start, int End) range, double top)
    {
        var centreX = Bounds.Width / 2;
        var centreY = top + Bounds.Height / 2;
        var indices = new List<(int Index, double Distance)>(Math.Max(0, range.End - range.Start));
        for (var i = range.Start; i < range.End; i++)
        {
            var frame = _geometry.Frame(i);
            var dx = frame.X + frame.Width / 2 - centreX;
            var dy = frame.Y + frame.Height / 2 - centreY;
            indices.Add((i, dx * dx + dy * dy));
        }
        indices.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return indices.Select(entry => entry.Index);
    }

    /// <summary>Requests that are no longer wanted now wait at background priority instead of being cancelled.</summary>
    private void Demote(HashSet<long> active)
    {
        var service = _model!.Thumbnails;
        foreach (var (id, task) in _thumbnailTasks.ToList())
        {
            if (!active.Contains(id) && task.Priority != ThumbnailPriority.Background)
            {
                service.SetPriority(task.Key, ThumbnailPriority.Background);
                _thumbnailTasks[id] = task with { Priority = ThumbnailPriority.Background };
            }
        }
    }

    /// <summary>Keeps at most <see cref="WaitingScreens"/> screens of waiting tiles, dropping the farthest from the screen.</summary>
    private void TrimWaiting(int perScreen)
    {
        var limit = (int)(Math.Max(1, perScreen) * WaitingScreens);
        var waiting = _thumbnailTasks.Where(pair => pair.Value.Priority == ThumbnailPriority.Background).ToList();
        if (waiting.Count <= limit || _model is null)
        {
            return;
        }
        var (visibleStart, _) = VisibleRange();
        var order = _model.Order;
        foreach (var (id, task) in waiting
            .OrderByDescending(pair => Math.Abs((order.IndexOf(pair.Key) ?? int.MaxValue / 2) - visibleStart))
            .Take(waiting.Count - limit))
        {
            task.Cancel.Cancel();
            _thumbnailTasks.Remove(id);
        }
    }
}
