namespace BuildingBlocks.Application.Abstractions.Mobile.Sync;

/// <summary>
/// Shorthand for "the mobile client should re-fetch table X" — the only kind of
/// envelope the catalog command handlers (Service / Tool / Material /
/// GeneralSupport / Customer) need to emit. Catalog rows are tiny and read in
/// bundle through <c>GET /api/mobile/v2/catalogs</c>, so a single <c>refresh</c>
/// per mutation is both simpler and cheaper than fanning out per-row upsert
/// envelopes — and it naturally collapses a bulk import into one broadcast.
/// </summary>
/// <remarks>
/// Audience is hard-coded to <see cref="MobileSyncAudience.AllStations"/>
/// because catalog rows are global: a service / tool / material added by any
/// admin should reach every station immediately. Routing decisions live with
/// the broadcaster, but for catalogs the routing is fixed and uninteresting.
/// </remarks>
public static class MobileSyncCatalogBroadcasts
{
    /// <summary>
    /// Enqueue a single <c>refresh</c> envelope for <paramref name="table"/>. Safe
    /// to call multiple times from the same handler — the broadcaster's
    /// <c>FlushAsync</c> de-duplicates by <c>(Table, EntityId, Op)</c>.
    /// </summary>
    public static void EnqueueRefresh(IMobileSyncBroadcaster broadcaster, string table) =>
        broadcaster.Enqueue(new MobileSyncChange(
            Table: table,
            Op: MobileSyncOps.Refresh,
            EntityId: null,
            Audience: MobileSyncAudience.AllStations,
            Version: DateTimeOffset.UtcNow));
}
