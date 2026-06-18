using Identity.Application.Commands.CreateRole;
using Identity.Application.Commands.UpdateRolePermissions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Presentation.Endpoints;

public static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity/roles")
            .WithTags("Roles")
            .RequireAuthorization();

        group.MapPost("/", async (CreateRoleCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/identity/roles/{result.Value.RoleId}", result.Value)
                : Results.BadRequest(result.Error);
        });

        group.MapPut("/{roleId:guid}/permissions", async (
            Guid roleId,
            UpdateRolePermissionsRequest request,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new UpdateRolePermissionsCommand(roleId, request.PermissionCodes), ct);
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        });

        return app;
    }

    private sealed record UpdateRolePermissionsRequest(IReadOnlyList<string> PermissionCodes);
}
