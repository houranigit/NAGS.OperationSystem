using BuildingBlocks.Api.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Notifications.Application.Features;

namespace Notifications.Api.Endpoints;

internal static class NotificationEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/me", async (
            ISender sender,
            CancellationToken cancellationToken,
            int page = 1,
            int pageSize = 20,
            bool unreadOnly = false) =>
        {
            var result = await sender.Send(new GetMyNotificationsQuery(page, pageSize, unreadOnly), cancellationToken);
            return result.ToOk();
        }).WithName("GetMyNotifications");

        group.MapGet("/me/unread-count", async (ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetMyUnreadNotificationCountQuery(), cancellationToken);
            return result.ToOk();
        }).WithName("GetMyUnreadNotificationCount");

        group.MapPost("/{id:guid}/read", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new MarkNotificationReadCommand(id), cancellationToken);
            return result.ToNoContent();
        }).WithName("MarkNotificationRead");

        group.MapPost("/me/mark-all-read", async (ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new MarkAllNotificationsReadCommand(), cancellationToken);
            return result.ToNoContent();
        }).WithName("MarkAllNotificationsRead");

        group.MapPost("/{id:guid}/archive", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ArchiveNotificationCommand(id), cancellationToken);
            return result.ToNoContent();
        }).WithName("ArchiveNotification");

        group.MapPost("/me/archive-all", async (ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ArchiveAllNotificationsCommand(), cancellationToken);
            return result.ToNoContent();
        }).WithName("ArchiveAllNotifications");

        group.MapPost("/me/devices", async (
            RegisterDeviceTokenRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new RegisterDeviceTokenCommand(
                request.Token,
                request.Platform,
                request.DeviceId,
                request.Locale,
                request.AppVersion), cancellationToken);
            return result.ToNoContent();
        }).WithName("RegisterNotificationDevice");

        // POST avoids placing the sensitive FCM registration token in a URL or access log.
        group.MapPost("/me/devices/revoke", async (
            RevokeDeviceTokenRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new RevokeDeviceTokenCommand(request.Token), cancellationToken);
            return result.ToNoContent();
        }).WithName("RevokeNotificationDevice");
    }
}
