namespace JGlancer.Media.Thumbnails;

/// <summary>
/// Bounds concurrent work and hands free slots to the highest-priority waiter first (FIFO within a priority). Waiters
/// can be boosted (a visible tile joins a prefetch job) and cancelled (macOS PriorityLimiter).
/// </summary>
internal sealed class PriorityLimiter(int capacity)
{
    private readonly Lock _lock = new();
    private readonly int _capacity = Math.Max(1, capacity);
    private readonly List<Waiter> _waiters = [];
    private int _running;
    private long _counter;

    /// <summary>Tasks currently holding a slot (for tests).</summary>
    public int Running
    {
        get
        {
            lock (_lock)
            {
                return _running;
            }
        }
    }

    /// <summary>Tasks waiting for a slot (for tests: they wait for a waiter instead of guessing with delays).</summary>
    public int Waiting
    {
        get
        {
            lock (_lock)
            {
                return _waiters.Count;
            }
        }
    }

    /// <summary>Waits for a slot; false if cancelled while waiting (no slot taken).</summary>
    public async Task<bool> AcquireAsync(ThumbnailPriority priority, object? tag, CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested)
        {
            return false;
        }
        Waiter waiter;
        lock (_lock)
        {
            if (_running < _capacity && _waiters.Count == 0)
            {
                _running++;
                return true;
            }
            waiter = new Waiter(priority, ++_counter, tag);
            _waiters.Add(waiter);
        }
        await using var registration = cancellation.Register(() => Cancel(waiter)).ConfigureAwait(false);
        return await waiter.Completion.Task.ConfigureAwait(false);
    }

    /// <summary>Frees a slot taken by a successful <see cref="AcquireAsync"/>.</summary>
    public void Release()
    {
        lock (_lock)
        {
            _running--;
            ResumeNext();
        }
    }

    /// <summary>Raises the priority of waiters with <paramref name="tag"/>.</summary>
    public void Boost(object tag, ThumbnailPriority priority)
    {
        lock (_lock)
        {
            foreach (var waiter in _waiters.Where(w => Equals(w.Tag, tag) && w.Priority < priority))
            {
                waiter.Priority = priority;
            }
        }
    }

    /// <summary>Sets the priority of waiters with <paramref name="tag"/>, lower or higher (a tile scrolled away waits).</summary>
    public void SetPriority(object tag, ThumbnailPriority priority)
    {
        lock (_lock)
        {
            foreach (var waiter in _waiters.Where(w => Equals(w.Tag, tag)))
            {
                waiter.Priority = priority;
            }
        }
    }

    private void Cancel(Waiter waiter)
    {
        lock (_lock)
        {
            if (!_waiters.Remove(waiter))
            {
                return;
            }
        }
        waiter.Completion.TrySetResult(false);
    }

    private void ResumeNext()
    {
        while (_running < _capacity && _waiters.Count > 0)
        {
            var best = _waiters.MaxBy(w => (w.Priority, -w.Order))!;
            _waiters.Remove(best);
            _running++;
            // Continuations run asynchronously, outside the lock.
            best.Completion.TrySetResult(true);
        }
    }

    private sealed class Waiter(ThumbnailPriority priority, long order, object? tag)
    {
        public ThumbnailPriority Priority { get; set; } = priority;
        public long Order { get; } = order;
        public object? Tag { get; } = tag;
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
