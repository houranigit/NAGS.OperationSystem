using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Results;
using Identity.Application.Features.Sessions;
using Identity.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Api.Endpoints;

internal static class SessionEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        MapAdmin(group);
        MapSelf(group);
    }

    // Admin session administration, scoped per user under /users.
    private static void MapAdmin(IEndpointRouteBuilder group)
    {
        var users = group.MapGroup("/users").WithTags("Identity.Sessions");

        users.MapGet("/{id:guid}/sessions", async (Guid id, ISender sender, CancellationToken ct, bool activeOnly = false) =>
        {
            var result = await sender.Send(new GetUserSessionsQuery(id, activeOnly), ct);
            return result.ToOk();
        }).RequirePermission(IdentityPermissions.Sessions.View);

        users.MapPost("/{id:guid}/sessions/revoke-all", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RevokeUserSessionsCommand(id), ct);
            return result.ToNoContent();
        }).RequirePermission(IdentityPermissions.Sessions.Revoke);

        var sessions = group.MapGroup("/sessions").WithTags("Identity.Sessions");

        sessions.MapDelete("/{sessionId:guid}", async (Guid sessionId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RevokeSessionCommand(sessionId), ct);
            return result.ToNoContent();
        }).RequirePermission(IdentityPermissions.Sessions.Revoke);
    }

    // Self-service: any authenticated user manages their own sessions under /me.
    private static void MapSelf(IEndpointRouteBuilder group)
    {
        var me = group.MapGroup("/me/sessions").WithTags("Identity.Sessions");

        me.MapGet("/", async (ISender sender, HttpContext http, CancellationToken ct) =>
        {
            var token = http.Request.Cookies[AuthCookies.RefreshTokenCookie];
            var result = await sender.Send(new GetMySessionsQuery(token), ct);
            return result.ToOk();
        }).RequireAuthorization();

        me.MapDelete("/{sessionId:guid}", async (Guid sessionId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RevokeMySessionCommand(sessionId), ct);
            return result.ToNoContent();
        }).RequireAuthorization();

        me.MapPost("/revoke-others", async (ISender sender, HttpContext http, CancellationToken ct) =>
        {
            var token = http.Request.Cookies[AuthCookies.RefreshTokenCookie];
            var result = await sender.Send(new RevokeMyOtherSessionsCommand(token), ct);
            return result.ToNoContent();
        }).RequireAuthorization();
    }
}
