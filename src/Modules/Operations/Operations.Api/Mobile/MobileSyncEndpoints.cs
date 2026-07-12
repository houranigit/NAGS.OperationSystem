using BuildingBlocks.Application.Mobile;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Operations.Api.Mobile;

/// <summary>
/// REST catch-up for the offline-sync channel. The client calls this after a SignalR reconnect
/// with the oldest cursor its table caches have all applied; the server answers with one
/// <c>refresh</c> envelope per
/// requested table, telling the client to re-pull those tables through the bulk read endpoints.
/// This is the deliberate pragmatic model: no server-side change log or tombstones — a full-table
/// refresh reconciles any pushes missed while disconnected.
/// </summary>
internal static class MobileSyncEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/sync/changes", (TimeProvider timeProvider, string? since = null, string? tables = null) =>
        {
            // `since` is accepted for wire-compatibility with the client's cursor handshake but the
            // response is always a full refresh per table in this release.
            _ = since;

            var requested = string.IsNullOrWhiteSpace(tables)
                ? MobileSyncTables.All
                : tables.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(MobileSyncTables.All.Contains)
                    .ToList();

            var now = timeProvider.GetUtcNow();
            var changes = requested
                .Select(table => new MobileSyncChange(
                    Table: table,
                    Op: MobileSyncOps.Refresh,
                    EntityId: null,
                    Audience: MobileSyncAudience.AllStations,
                    Version: now))
                .ToList();

            return Results.Ok(changes);
        }).RequireAuthorization();
    }
}
