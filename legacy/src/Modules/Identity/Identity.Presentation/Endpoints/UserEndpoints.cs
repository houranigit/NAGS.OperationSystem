using Identity.Application.Commands.AssignRole;
using Identity.Application.Commands.CreateUser;
using Identity.Application.Commands.InviteUser;
using Identity.Application.Commands.RemoveRole;
using Identity.Application.Commands.ResendInvitation;
using Identity.Application.Commands.UnlockUser;
using Identity.Application.Commands.UpdateUser;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Presentation.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity/users")
            .WithTags("Users")
            .RequireAuthorization();

        group.MapPost("/", async (CreateUserCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/identity/users/{result.Value.UserId}", result.Value)
                : Results.BadRequest(result.Error);
        });

        group.MapPost("/invite", async (InviteUserCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/identity/users/{result.Value.UserId}", result.Value)
                : Results.BadRequest(result.Error);
        });

        group.MapPost("/{userId:guid}/unlock", async (Guid userId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new UnlockUserCommand(userId), ct);
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        });

        group.MapPost("/{userId:guid}/resend-invite", async (Guid userId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ResendInvitationCommand(userId), ct);
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        });

        group.MapPut("/{userId:guid}", async (Guid userId, UpdateUserCommand command, ISender sender, CancellationToken ct) =>
        {
            if (userId != command.Id)
                return Results.BadRequest("Route id does not match body id.");

            var result = await sender.Send(command, ct);
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        });

        group.MapPost("/{userId:guid}/roles/{roleId:guid}", async (
            Guid userId, Guid roleId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new AssignRoleCommand(userId, roleId), ct);
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        });

        group.MapDelete("/{userId:guid}/roles/{roleId:guid}", async (
            Guid userId, Guid roleId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RemoveRoleCommand(userId, roleId), ct);
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        });

        return app;
    }
}
