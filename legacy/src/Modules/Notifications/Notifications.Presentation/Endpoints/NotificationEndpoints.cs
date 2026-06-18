using System.Security.Claims;
using BuildingBlocks.Domain.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Notifications.Application.Features.ArchiveAll;
using Notifications.Application.Features.GetMyInbox;
using Notifications.Application.Features.GetUnreadCount;
using Notifications.Application.Features.MarkAllAsRead;
using Notifications.Application.Features.MarkAsRead;
using Notifications.Application.Features.RegisterDeviceToken;
using Notifications.Application.Features.RevokeDeviceToken;
using Notifications.Domain.Aggregates.DeviceToken;

namespace Notifications.Presentation.Endpoints;

/// <summary>
/// REST surface for the inbox UI (portal bell + mobile inbox screen). Uses the default
/// authorization policy so it accepts both the Blazor cookie and the mobile JWT — the
/// caller's user id is always pulled from the <c>sub</c> claim, never from the request
/// body, so no client can read someone else's inbox.
/// </summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications")
            .WithTags("Notifications")
            .RequireAuthorization();

        group.MapGet("/me", async (
            HttpContext http,
            ISender sender,
            int? page,
            int? pageSize,
            bool? unreadOnly,
            CancellationToken ct) =>
        {
            var userId = ResolveUserId(http);
            if (userId is null) return Results.Unauthorized();

            var query = new GetMyInboxQuery(
                userId.Value,
                Page: page ?? 1,
                PageSize: pageSize ?? 20,
                UnreadOnly: unreadOnly ?? false);
            var result = await sender.Send(query, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(result.Error);
        });

        group.MapGet("/me/unread-count", async (
            HttpContext http,
            ISender sender,
            CancellationToken ct) =>
        {
            var userId = ResolveUserId(http);
            if (userId is null) return Results.Unauthorized();

            var result = await sender.Send(new GetUnreadCountQuery(userId.Value), ct);
            return result.IsSuccess
                ? Results.Ok(new { count = result.Value })
                : Results.BadRequest(result.Error);
        });

        group.MapPost("/{id:guid}/read", async (
            Guid id,
            HttpContext http,
            ISender sender,
            CancellationToken ct) =>
        {
            var userId = ResolveUserId(http);
            if (userId is null) return Results.Unauthorized();

            var result = await sender.Send(new MarkAsReadCommand(userId.Value, id), ct);
            return result.IsSuccess ? Results.NoContent() : MapError(result.Error);
        });

        group.MapPost("/me/mark-all-read", async (
            HttpContext http,
            ISender sender,
            CancellationToken ct) =>
        {
            var userId = ResolveUserId(http);
            if (userId is null) return Results.Unauthorized();

            var result = await sender.Send(new MarkAllAsReadCommand(userId.Value), ct);
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        });

        group.MapPost("/me/archive-all", async (
            HttpContext http,
            ISender sender,
            CancellationToken ct) =>
        {
            var userId = ResolveUserId(http);
            if (userId is null) return Results.Unauthorized();

            var result = await sender.Send(new ArchiveAllCommand(userId.Value), ct);
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        });

        // ---- Device tokens (FCM) -------------------------------------------------------
        // The mobile app registers its FCM token on login and on every app start (Firebase
        // rotates them periodically). The same pair (UserId, Token) is upserted as
        // idempotent — repeated POSTs from the same device just bump LastSeenAt.
        group.MapPost("/me/devices", async (
            RegisterDeviceTokenRequest body,
            HttpContext http,
            ISender sender,
            CancellationToken ct) =>
        {
            var userId = ResolveUserId(http);
            if (userId is null) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(body.Token))
                return Results.BadRequest(new { error = "Token is required." });

            var platform = Enum.TryParse<DevicePlatform>(body.Platform, ignoreCase: true, out var p)
                ? p
                : DevicePlatform.Android;

            var result = await sender.Send(new RegisterDeviceTokenCommand(userId.Value, body.Token, platform), ct);
            return result.IsSuccess ? Results.NoContent() : MapError(result.Error);
        });

        group.MapDelete("/me/devices/{token}", async (
            string token,
            HttpContext http,
            ISender sender,
            CancellationToken ct) =>
        {
            var userId = ResolveUserId(http);
            if (userId is null) return Results.Unauthorized();

            var result = await sender.Send(new RevokeDeviceTokenCommand(userId.Value, token), ct);
            return result.IsSuccess ? Results.NoContent() : MapError(result.Error);
        });

        return app;
    }

    public sealed record RegisterDeviceTokenRequest(string Token, string Platform);

    private static Guid? ResolveUserId(HttpContext http)
    {
        var sub = http.User.FindFirstValue("sub")
                  ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) && id != Guid.Empty ? id : null;
    }

    private static IResult MapError(Error error) => error.Type switch
    {
        ErrorType.NotFound => Results.NotFound(new { error.Code, error.Description }),
        ErrorType.Validation => Results.BadRequest(new { error.Code, error.Description }),
        ErrorType.Conflict => Results.Conflict(new { error.Code, error.Description }),
        ErrorType.Unauthorized => Results.Unauthorized(),
        _ => Results.BadRequest(new { error.Code, error.Description })
    };
}
