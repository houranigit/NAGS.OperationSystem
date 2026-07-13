using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Notifications.Api.Realtime;

[Authorize]
public sealed class NotificationsHub : Hub
{
    public const string Path = "/hubs/notifications";
    public const string ClientMethod = "notification";

    public override async Task OnConnectedAsync()
    {
        if (ResolveUserId(Context.User) is { } userId)
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(userId));
        else
            Context.Abort();

        await base.OnConnectedAsync();
    }

    public static string GroupName(Guid userId) => $"notifications:user:{userId:N}";

    private static Guid? ResolveUserId(ClaimsPrincipal? user)
    {
        var subject = user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(subject, out var userId) && userId != Guid.Empty ? userId : null;
    }
}
