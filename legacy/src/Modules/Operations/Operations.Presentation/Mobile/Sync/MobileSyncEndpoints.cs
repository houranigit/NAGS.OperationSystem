using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;

namespace Operations.Presentation.Mobile.Sync;

/// <summary>
/// REST companion to the SignalR <see cref="MobileSyncHub"/>. Mobile uses
/// <c>GET /api/mobile/v2/sync/changes</c> as a belt-and-braces reconnect path: if
/// the WebSocket dropped or the app was backgrounded long enough to miss pushes,
/// the client passes the per-table cursor it last saw and the server replays
/// everything newer. For phase 1 the endpoint emits a single <c>refresh</c>
/// envelope per requested table — the mobile <c>SyncCoordinator</c> already knows
/// how to re-fetch a whole table on demand, so we route the recovery through that
/// path instead of writing per-row delta queries up front.
/// </summary>
public static class MobileSyncEndpoints
{
    public static IEndpointRouteBuilder MapMobileSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/mobile/v2/sync")
            .WithTags("Mobile V2 Sync")
            .RequireAuthorization("MobileJwt");

        group.MapGet("/changes", GetChangesAsync);

        return app;
    }

    /// <summary>
    /// Returns the per-table change envelopes the caller missed since the supplied
    /// cursor. Phase 1 keeps the implementation minimal: one <c>refresh</c>
    /// envelope per requested table, instructing the mobile client to run its
    /// existing full-table sync for the affected tables. The envelope shape is
    /// identical to the SignalR push so mobile has one apply path.
    /// </summary>
    /// <param name="tables">
    /// Optional comma-separated list (e.g. <c>flights,flights-aog,employees</c>).
    /// When omitted, the endpoint returns refresh envelopes for every known table.
    /// </param>
    /// <param name="since">
    /// Ignored in phase 1 — the response always tells the client to re-fetch in
    /// full. The parameter is still accepted on the wire so the client can pass
    /// its cursor without protocol churn when we wire row-level deltas later.
    /// </param>
    private static async Task<IResult> GetChangesAsync(
        IMobileEmployeeContext employeeContext,
        CancellationToken ct,
        string? tables = null,
        DateTimeOffset? since = null)
    {
        _ = since;

        // Same authentication guard as every other v2 mobile endpoint: a valid JWT
        // whose `sub` doesn't resolve to a linked employee should not get sync data.
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return Results.Json(
                new
                {
                    title = "Employee profile required",
                    code = "Mobile.EmployeeNotLinked",
                    status = StatusCodes.Status403Forbidden
                },
                statusCode: StatusCodes.Status403Forbidden);

        var requested = ParseTables(tables);
        var now = DateTimeOffset.UtcNow;

        var envelopes = requested
            .Select(t => new MobileSyncChange(
                Table: t,
                Op: MobileSyncOps.Refresh,
                EntityId: null,
                Audience: ResolveAudienceForRefresh(t),
                Version: now,
                Payload: null))
            .ToList();

        return Results.Ok(envelopes);
    }

    private static IReadOnlyList<string> ParseTables(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new[]
            {
                MobileSyncTables.Flights,
                MobileSyncTables.FlightsAog,
                MobileSyncTables.FlightsAdHoc,
                MobileSyncTables.Employees,
                MobileSyncTables.Services,
                MobileSyncTables.Tools,
                MobileSyncTables.Materials,
                MobileSyncTables.GeneralSupports,
                MobileSyncTables.Customers,
            };

        // Trim + skip empty segments so "flights,, ,flights-aog" still parses cleanly.
        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string ResolveAudienceForRefresh(string table) => table switch
    {
        MobileSyncTables.Flights => MobileSyncAudience.AllStations,
        MobileSyncTables.FlightsAog => MobileSyncAudience.AllStations,
        MobileSyncTables.FlightsAdHoc => MobileSyncAudience.AllStations,
        MobileSyncTables.Employees => MobileSyncAudience.AllStations,
        _ => MobileSyncAudience.AllStations,
    };
}
