namespace BuildingBlocks.Application.Abstractions.Mobile.Sync;

/// <summary>
/// Wire envelope for one mobile-sync change pushed over SignalR (or returned by the
/// REST catch-up endpoint after a reconnect). One shape, used by every push and the
/// catch-up endpoint alike, so the mobile client has a single apply path.
/// </summary>
/// <param name="Table">Logical table identifier — see <see cref="MobileSyncTables"/>.</param>
/// <param name="Op">Operation kind — see <see cref="MobileSyncOps"/>.</param>
/// <param name="EntityId">
/// Server id of the affected row when <see cref="Op"/> is <c>upsert</c> or <c>delete</c>.
/// Null when <see cref="Op"/> is <c>refresh</c>.
/// </param>
/// <param name="Audience">
/// SignalR routing key — see <see cref="MobileSyncAudience"/>. Used server-side to
/// pick the broadcast group; mobile receives it for diagnostics/logging only.
/// </param>
/// <param name="Version">
/// Per-table monotonic cursor. We use the affected row's <c>UpdatedAt</c> serialised
/// as ISO-8601 UTC so the mobile client can ignore late-arriving stale events
/// (cursor lives in <c>SyncStateEntity.cursor</c>) and request a catch-up since
/// the last successful cursor on reconnect.
/// </param>
/// <param name="Payload">
/// Inline row JSON when the row is small enough to wire-embed (catalog rows).
/// Null for flight rows: mobile re-fetches by id through <c>GET /flights/{id}</c>
/// so we keep the single projection path used by the bulk endpoints.
/// </param>
/// <param name="OriginMutationId">
/// Reserved for the future write/outbox flow — when the server broadcasts an event
/// that was caused by a mobile-originated mutation, this carries that mutation's id
/// so the originating device can skip applying its own echo. Always null today.
/// </param>
/// <param name="OriginClientId">
/// Reserved for the future write/outbox flow — per-installation UUID of the device
/// that authored the mutation. Lets us echo-filter when the same employee is signed
/// in on two devices. Always null today.
/// </param>
public sealed record MobileSyncChange(
    string Table,
    string Op,
    string? EntityId,
    string Audience,
    DateTimeOffset Version,
    string? Payload = null,
    string? OriginMutationId = null,
    string? OriginClientId = null);
