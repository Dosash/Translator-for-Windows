namespace Translator.Offline;

/// <summary>
/// Small LRU of loaded models with reference-counted leases: an evicted model is disposed only after the last
/// translation using it finishes.
/// </summary>
internal sealed class ModelCache<T> : IDisposable where T : class, IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly Func<string, T> _loader;
    private readonly TimeProvider _time;
    private bool _disposed;

    public ModelCache(int capacity, Func<string, T> loader, TimeProvider? time = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
        _loader = loader;
        _time = time ?? TimeProvider.System;
    }

    public int Capacity { get; }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count;
            }
        }
    }

    public bool Contains(string id)
    {
        lock (_sync)
        {
            return _entries.ContainsKey(id);
        }
    }

    public Lease Acquire(string id, CancellationToken cancellationToken = default)
    {
        if (TryAcquireLoaded(id) is { } cached)
        {
            return cached;
        }

        // One load at a time: loading is CPU- and memory-heavy, and a second caller usually wants the same model.
        _loadGate.Wait(cancellationToken);
        try
        {
            if (TryAcquireLoaded(id) is { } loaded)
            {
                return loaded;
            }

            var value = _loader(id);
            var evicted = new List<T>();
            Entry entry;
            lock (_sync)
            {
                if (_disposed)
                {
                    value.Dispose();
                    throw new ObjectDisposedException(nameof(ModelCache<T>));
                }

                entry = new Entry(id, value) { RefCount = 1, LastUsed = _time.GetTimestamp() };
                _entries[id] = entry;
                while (_entries.Count > Capacity)
                {
                    var victim = _entries.Values.Where(e => e.RefCount == 0).MinBy(e => e.LastUsed);
                    if (victim is null)
                    {
                        break;
                    }

                    _entries.Remove(victim.Id);
                    evicted.Add(victim.Value);
                }
            }

            evicted.ForEach(v => v.Dispose());
            return new Lease(this, entry);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    /// <summary>Drops a model (e.g. before its files are replaced or deleted).</summary>
    public void Evict(string id)
    {
        T? toDispose = null;
        lock (_sync)
        {
            if (_entries.Remove(id, out var entry))
            {
                if (entry.RefCount == 0)
                {
                    toDispose = entry.Value;
                }
                else
                {
                    entry.Evicted = true;
                }
            }
        }

        toDispose?.Dispose();
    }

    /// <summary>Releases models that have not been used for <paramref name="idle"/>, so a tray app does not hold hundreds of MB.</summary>
    public void TrimIdle(TimeSpan idle)
    {
        var evicted = new List<T>();
        lock (_sync)
        {
            foreach (var entry in _entries.Values.ToList())
            {
                if (entry.RefCount == 0 && _time.GetElapsedTime(entry.LastUsed) >= idle)
                {
                    _entries.Remove(entry.Id);
                    evicted.Add(entry.Value);
                }
            }
        }

        evicted.ForEach(v => v.Dispose());
    }

    public void Dispose()
    {
        var evicted = new List<T>();
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var entry in _entries.Values)
            {
                if (entry.RefCount == 0)
                {
                    evicted.Add(entry.Value);
                }
                else
                {
                    entry.Evicted = true;
                }
            }

            _entries.Clear();
        }

        evicted.ForEach(v => v.Dispose());
    }

    private Lease? TryAcquireLoaded(string id)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_entries.TryGetValue(id, out var entry))
            {
                return null;
            }

            entry.RefCount++;
            entry.LastUsed = _time.GetTimestamp();
            return new Lease(this, entry);
        }
    }

    private void Release(Entry entry)
    {
        var dispose = false;
        lock (_sync)
        {
            entry.RefCount--;
            entry.LastUsed = _time.GetTimestamp();
            dispose = entry.Evicted && entry.RefCount == 0;
        }

        if (dispose)
        {
            entry.Value.Dispose();
        }
    }

    internal sealed class Entry(string id, T value)
    {
        public string Id { get; } = id;
        public T Value { get; } = value;
        public int RefCount { get; set; }
        public long LastUsed { get; set; }
        public bool Evicted { get; set; }
    }

    public sealed class Lease : IDisposable
    {
        private ModelCache<T>? _owner;
        private readonly Entry _entry;

        internal Lease(ModelCache<T> owner, Entry entry)
        {
            _owner = owner;
            _entry = entry;
        }

        public T Value => _entry.Value;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(_entry);
    }
}
