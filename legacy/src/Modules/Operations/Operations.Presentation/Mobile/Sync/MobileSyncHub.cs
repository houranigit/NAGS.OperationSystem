using System.Security.Claims;
using Core.Contracts.Readers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;

namespace Operations.Presentation.Mobile.Sync;

/// <summary>
/// SignalR hub for the v2 Android client's real-time mirror of its local Room cache.
/// On connect each caller is added to two groups — <c>employee:{guid}</c> (so the
/// server can target events that only affect this user, e.g. an assignment change)
/// and <c>station:{IATA}</c> (for events that affect every employee at the station,
/// e.g. a new AOG flight). All-station broadcasts (catalogs) use the connection-wide
/// <c>Clients.All</c> fan-out — no group join needed.
/// </summary>
/// <remarks>
/// JWT bearer is read from the standard <c>Authorization</c> header for HTTP
/// negotiation and from <c>?access_token=</c> for the WebSocket upgrade — the
/// existing <see cref="Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents.OnMessageReceived"/>
/// hook in <c>ServiceCollectionExtensions.AddOperationsWebAuthentication</c> already
/// handles the lift. Authorization is the same <c>MobileJwt</c> policy used by the
/// v2 REST endpoints so the entire mobile surface enforces one rule.
/// <para>
/// We resolve the employee directly off <see cref="HubCallerContext.User"/> +
/// <see cref="IEmployeeReader.GetByLinkedUserIdAsync"/> instead of through
/// <c>IMobileEmployeeContext</c>: the HttpContext-backed context caches in
/// <c>HttpContext.Items</c>, which lives only for the negotiation request and is
/// gone once the WebSocket is upgraded.
/// </para>
/// </remarks>
[Authorize(Policy = "MobileJwt")]
public sealed class MobileSyncHub(IEmployeeReader employeeReader) : Hub
{
    /// <summary>Mounted at <c>/hubs/mobile-sync</c> by <c>Host.Web/Program.cs</c>.</summary>
    public const string Path = "/hubs/mobile-sync";

    /// <summary>Client method invoked by the server to push a single sync envelope.</summary>
    public const string ChangeClientMethod = "change";

    /// <summary>Client method invoked by the server when a catch-up replay finishes.</summary>
    public const string CatchupDoneClientMethod = "catchupDone";

    /// <summary>
    /// JWT subject claim. The Identity issuer always writes the user id as a
    /// string-form <see cref="Guid"/> here — same convention as
    /// <c>HttpContextMobileEmployeeContext</c>.
    /// </summary>
    private const string SubjectClaimType = "sub";

    public override async Task OnConnectedAsync()
    {
        var ct = Context.ConnectionAborted;

        var userId = ResolveUserId(Context.User);
        if (userId is null)
        {
            await base.OnConnectedAsync();
            return;
        }

        var employee = await employeeReader.GetByLinkedUserIdAsync(userId.Value, ct);
        if (employee is not null)
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                EmployeeGroup(employee.EmployeeId),
                ct);
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                StationGroup(employee.StationSnapshot.IataCode),
                ct);
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// In-band catch-up: the mobile client passes the per-table cursors it last saw,
    /// the server replays everything newer than that for the caller's audience, then
    /// signals end-of-stream via <see cref="CatchupDoneClientMethod"/>. Equivalent to
    /// the REST <c>/api/mobile/v2/sync/changes</c> endpoint but lets the client stay
    /// inside one channel after reconnect.
    /// </summary>
    /// <remarks>
    /// Phase 1 stub: the mobile client owns the actual catch-up via the REST
    /// endpoint <c>/api/mobile/v2/sync/changes</c>, which gives us paging and
    /// caching headers for free. The hub method exists today so the client can
    /// wire its reconnect handshake against the final shape without a contract
    /// bump later — the server simply acknowledges and signals done.
    /// </remarks>
    public async Task RequestCatchup(IDictionary<string, string?>? cursors)
    {
        _ = cursors;
        await Clients.Caller.SendAsync(CatchupDoneClientMethod, Context.ConnectionAborted);
    }

    public static string EmployeeGroup(Guid employeeId) =>
        $"{MobileSyncAudience.EmployeePrefix}{employeeId}";

    public static string StationGroup(string stationIata) =>
        $"{MobileSyncAudience.StationPrefix}{stationIata}";

    private static Guid? ResolveUserId(ClaimsPrincipal? user)
    {
        var sub = user?.FindFirstValue(SubjectClaimType)
                  ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) && id != Guid.Empty ? id : null;
    }
}
