namespace Host.Web.Services;

/// <summary>
/// Per-circuit cache for rarely-changing lookup lists (dropdown options etc.).
/// Replaces the "every dialog open issues 3–5 DB lookups" pattern with a single
/// load per TTL window, plus explicit invalidation when the source data changes.
/// </summary>
/// <remarks>
/// Scoped per Blazor Server circuit so one user's invalidation (after they edited
/// a currency, say) does not erase another user's cached list. If cross-user sharing
/// becomes desirable this can be swapped to singleton without changing consumers.
/// <para>
/// All loaders are de-duplicated: if two concurrent dialogs ask for the same list on a
/// cold cache, only one database round-trip is issued and both awaits return the same items.
/// </para>
/// </remarks>
public interface ILookupCache
{
    /// <summary>
    /// Returns the cached list for <typeparamref name="T"/> if fresh, otherwise calls
    /// <paramref name="loader"/> exactly once, caches the result, and returns it.
    /// Concurrent callers on a cold cache share a single load.
    /// </summary>
    Task<IReadOnlyList<T>> GetAsync<T>(
        Func<CancellationToken, Task<IReadOnlyList<T>>> loader,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>Drops the cached entry for <typeparamref name="T"/>; next <see cref="GetAsync{T}"/> reloads.</summary>
    void Invalidate<T>() where T : class;

    /// <summary>Drops every cached entry in this circuit.</summary>
    void InvalidateAll();
}
