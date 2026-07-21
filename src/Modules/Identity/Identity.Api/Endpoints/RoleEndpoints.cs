using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Domain.Results;
using Identity.Application.Features.Roles;
using Identity.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Api.Endpoints;

internal static class RoleEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var roles = group.MapGroup("/roles").WithTags("Identity.Roles");

        roles.MapGet("/", async (
            ISender sender,
            CancellationToken ct,
            int page = 1,
            int pageSize = 20,
            string? search = null,
            BuildingBlocks.Contracts.Authorization.UserType? userType = null,
            string? sort = null) =>
        {
            var result = await sender.Send(new GetRolesQuery(page, pageSize, search, userType, sort), ct);
            return result.ToOk();
        }).RequirePermission(IdentityPermissions.Roles.View);

        roles.MapGet("/options", async (
            ISender sender,
            CancellationToken ct,
            BuildingBlocks.Contracts.Authorization.UserType? userType = null,
            bool assignableOnly = false) =>
        {
            var result = await sender.Send(new GetRoleOptionsQuery(userType, assignableOnly), ct);
            return result.ToOk();
        }).RequirePermission(IdentityPermissions.Roles.View);

        roles.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetRoleByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(IdentityPermissions.Roles.View);

        roles.MapPost("/", async (CreateRoleRequest request, ISender sender, CancellationToken ct) =>
        {
            if (request.CompatibleUserType is not { } compatibleUserType)
                return ApiResults.Problem(Error.Validation("A compatible user type is required.", "Identity.Role.CompatibleUserTypeRequired"));

            var result = await sender.Send(new CreateRoleCommand(request.Name, request.Description, compatibleUserType, request.Permissions), ct);
            return result.ToCreated(id => $"/api/v1/identity/roles/{id}");
        }).RequirePermission(IdentityPermissions.Roles.Create)
            .RequirePermission(IdentityPermissions.Roles.ManagePermissions);

        roles.MapPut("/{id:guid}", async (Guid id, UpdateRoleRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new UpdateRoleCommand(id, request.Name, request.Description), ct);
            return result.ToNoContent();
        }).RequirePermission(IdentityPermissions.Roles.Update);

        roles.MapPut("/{id:guid}/permissions", async (Guid id, UpdateRolePermissionsRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new UpdateRolePermissionsCommand(id, request.Permissions), ct);
            return result.ToNoContent();
        }).RequirePermission(IdentityPermissions.Roles.ManagePermissions);

        roles.MapPut("/{id:guid}/editor", async (Guid id, UpdateRoleAndPermissionsRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateRoleAndPermissionsCommand(id, request.Name, request.Description, request.Permissions), ct);
            return result.ToNoContent();
        }).RequirePermission(IdentityPermissions.Roles.Update)
            .RequirePermission(IdentityPermissions.Roles.ManagePermissions);

        roles.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DeleteRoleCommand(id), ct);
            return result.ToNoContent();
        }).RequirePermission(IdentityPermissions.Roles.Delete);

        group.MapGet("/permissions", async (ISender sender, CancellationToken ct, BuildingBlocks.Contracts.Authorization.UserType? userType = null) =>
        {
            var result = await sender.Send(new GetPermissionCatalogQuery(userType), ct);
            return result.ToOk();
        }).RequirePermission(IdentityPermissions.Roles.View).WithTags("Identity.Roles");
    }
}
