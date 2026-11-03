using System.Security.Claims;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Contracts.Authorization;
using MasterData.Contracts.Readers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace OperationsSystem.Api.Mobile;

/// <summary>
/// Shared catalog-change hub, retained for MasterData independently of Operations workflows.
/// Existing station/employee audience groups remain available to catalog producers.
/// </summary>
/// <remarks>
/// The bearer token is read from the <c>Authorization</c> header on negotiate and from
/// <c>?access_token=</c> on the WebSocket upgrade (wired in the host's JwtBearer
/// <c>OnMessageReceived</c>). The staff member is resolved from the <c>external_ref</c>
/// claim, which Identity stamps with the linked StaffMember id for StationStaff users.
/// </remarks>
[Authorize]
public sealed class MobileSyncHub(IMasterDataReader masterData) : Hub
{
    /// <summary>Mounted at this path by the API host.</summary>
    public const string Path = "/hubs/mobile-sync";

    /// <summary>Client method invoked by the server to push a single sync envelope.</summary>
    public const string ChangeClientMethod = "change";

    /// <summary>Client method invoked by the server when a catch-up replay finishes.</summary>
    public const string CatchupDoneClientMethod = "catchupDone";

    public override async Task OnConnectedAsync()
    {
        var ct = Context.ConnectionAborted;

        var staffMemberId = ResolveStaffMemberId(Context.User);
        if (staffMemberId is not null)
        {
            var staff = await masterData.GetStaffMemberAsync(staffMemberId.Value, ct);
            if (staff is not null && staff.IsActive)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, MobileSyncAudience.Employee(staff.Id), ct);

                var station = await masterData.GetStationAsync(staff.StationId, ct);
                if (station is not null && station.IsActive)
                    await Groups.AddToGroupAsync(Context.ConnectionId, MobileSyncAudience.Station(station.IataCode), ct);
            }
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Acknowledges the existing handshake. Operations catch-up endpoints are retired;
    /// consumers reload catalog data through the MasterData API.
    /// </summary>
    public async Task RequestCatchup(IDictionary<string, string?>? cursors)
    {
        _ = cursors;
        await Clients.Caller.SendAsync(CatchupDoneClientMethod, Context.ConnectionAborted);
    }

    private static Guid? ResolveStaffMemberId(ClaimsPrincipal? user)
    {
        var externalRef = user?.FindFirstValue(AuthorizationClaimTypes.ExternalReference);
        return Guid.TryParse(externalRef, out var id) && id != Guid.Empty ? id : null;
    }
}
