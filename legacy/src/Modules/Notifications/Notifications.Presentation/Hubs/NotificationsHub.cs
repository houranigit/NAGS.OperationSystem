using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Notifications.Presentation.Hubs;

/// <summary>
/// SignalR hub for live notification updates. Each connected user joins a per-user
/// group keyed by their <c>sub</c> claim (Identity user id) so the application-layer
/// pusher can broadcast to a single recipient with <c>Clients.Group(userId)</c>.
/// JWT bearer is read from the standard Authorization header for portal/API and from
/// <c>?access_token=</c> for the mobile WebSocket negotiation (configured in
/// <c>JwtBearerEvents.OnMessageReceived</c>).
/// </summary>
[Authorize]
public sealed class NotificationsHub : Hub
{
    public const string Path = "/hubs/notifications";

    public override async Task OnConnectedAsync()
    {
        var userId = ResolveUserId();
        if (userId is not null)
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(userId.Value));

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = ResolveUserId();
        if (userId is not null)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(userId.Value));

        await base.OnDisconnectedAsync(exception);
    }

    public static string GroupName(Guid userId) => $"user:{userId}";

    private Guid? ResolveUserId()
    {
        var sub = Context.User?.FindFirstValue("sub")
                  ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) && id != Guid.Empty ? id : null;
    }
}
