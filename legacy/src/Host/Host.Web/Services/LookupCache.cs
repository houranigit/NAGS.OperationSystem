using System.Collections.Concurrent;

namespace Host.Web.Services;

/// <summary>
/// Default <see cref="ILookupCache"/>: keyed by <see cref="Type"/>, TTL-bounded, with per-key
/// single-flight de-duplication so a cold cache cannot trigger N parallel DB queries for the same list.
/// </summary>
internal sealed class LookupCache : ILookupCache, IDisposable
{
    /// <summary>Lookup lists rarely change inside a single session; 5 min is a pragmatic upper bound.</summary>
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private sealed record Entry(object Data, DateTimeOffset LoadedAt);

    private readonly ConcurrentDictionary<Type, Entry> _entries = new();
    private readonly ConcurrentDictionary<Type, SemaphoreSlim> _locks = new();

    public async Task<IReadOnlyList<T>> GetAsync<T>(
        Func<CancellationToken, Task<IReadOnlyList<T>>> loader,
        CancellationToken cancellationToken = default) where T : class
    {
        var type = typeof(T);

        if (TryGetFresh<T>(type, out var cached))
            return cached;

        var gate = _locks.GetOrAdd(type, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (TryGetFresh<T>(type, out cached))
                return cached;

            var data = await loader(cancellationToken);
            _entries[type] = new Entry(data, DateTimeOffset.UtcNow);
            return data;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Invalidate<T>() where T : class => _entries.TryRemove(typeof(T), out _);

    public void InvalidateAll() => _entries.Clear();

    private bool TryGetFresh<T>(Type type, out IReadOnlyList<T> value) where T : class
    {
        if (_entries.TryGetValue(type, out var entry) && !IsExpired(entry))
        {
            value = (IReadOnlyList<T>)entry.Data;
            return true;
        }

        value = Array.Empty<T>();
        return false;
    }

    private static bool IsExpired(Entry entry) =>
        DateTimeOffset.UtcNow - entry.LoadedAt > DefaultTtl;

    public void Dispose()
    {
        foreach (var gate in _locks.Values)
            gate.Dispose();

        _locks.Clear();
        _entries.Clear();
    }
}
