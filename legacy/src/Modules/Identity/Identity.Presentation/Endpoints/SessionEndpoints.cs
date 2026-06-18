using Identity.Application.Commands.RevokeAllSessions;
using Identity.Application.Commands.RevokeSession;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Presentation.Endpoints;

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity/sessions")
            .WithTags("Sessions")
            .RequireAuthorization();

        group.MapDelete("/{sessionId:guid}/users/{userId:guid}", async (
            Guid sessionId, Guid userId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RevokeSessionCommand(sessionId, userId), ct);
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        });

        group.MapDelete("/users/{userId:guid}", async (Guid userId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RevokeAllSessionsCommand(userId), ct);
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        });

        return app;
    }
}
