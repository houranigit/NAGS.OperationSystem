using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application.Features.Auth;
using Identity.Application.Features.Users;
using Identity.Domain.Authorization;
using Identity.Domain.Users;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Api.Endpoints;

internal static class UserEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var users = group.MapGroup("/users").WithTags("Identity.Users");

        users.MapGet("/", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null, UserStatus? status = null, Guid? roleId = null, UserType? userType = null, string? sort = null) =>
        {
            var result = await sender.Send(new GetUsersQuery(page, pageSize, search, status, roleId, userType, sort), ct);
            return result.ToOk();
        }).RequirePermission(IdentityPermissions.Users.View);

        users.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetUserByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(IdentityPermissions.Users.View);

        users.MapPost("/invite", async (InviteUserRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new InviteUserCommand(request.Email, request.DisplayName), ct);
            return result.ToCreated(u => $"/api/v1/identity/users/{u.Id}");
        }).RequirePermission(IdentityPermissions.Users.Invite);

        users.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new UpdateUserCommand(id, request.DisplayName), ct);
            return result.ToNoContent();
        }).RequirePermission(IdentityPermissions.Users.Update);

        users.MapPut("/{id:guid}/role", async (Guid id, AssignRoleRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new AssignRoleCommand(id, request.RoleId), ct);
            return result.ToNoContent();
        }).RequirePermission(IdentityPermissions.Users.AssignRole);

        users.MapPost("/{id:guid}/lock", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new LockUserCommand(id), ct);
            return result.ToNoContent();
        }).RequirePermission(IdentityPermissions.Users.Lock);

        users.MapPost("/{id:guid}/unlock", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new UnlockUserCommand(id), ct);
            return result.ToNoContent();
        }).RequirePermission(IdentityPermissions.Users.Unlock);

        users.MapPost("/{id:guid}/deactivate", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DeactivateUserCommand(id), ct);
            return result.ToNoContent();
        }).RequirePermission(IdentityPermissions.Users.Deactivate);

        users.MapPost("/{id:guid}/suspend", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new SuspendUserCommand(id), ct);
            return result.ToNoContent();
        }).RequirePermission(IdentityPermissions.Users.Suspend);

        users.MapPost("/{id:guid}/restore-access", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RestoreAccessCommand(id), ct);
            return result.ToNoContent();
        }).RequirePermission(IdentityPermissions.Users.RestoreAccess);

        users.MapPost("/{id:guid}/mfa/reset", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ResetUserMfaCommand(id), ct);
            return result.ToNoContent();
        }).RequirePermission(IdentityPermissions.Users.ResetMfa);

        users.MapPost("/{id:guid}/resend-invitation", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ResendInvitationCommand(id), ct);
            return result.ToNoContent();
        }).RequirePermission(IdentityPermissions.Users.Invite);
    }
}
