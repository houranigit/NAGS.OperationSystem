using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using Identity.Domain.Authorization;

namespace Identity.Application.Commands.UpdateRolePermissions;

public sealed record UpdateRolePermissionsCommand(
    Guid RoleId,
    IReadOnlyList<string> PermissionCodes
) : ICommand, IRequirePermission
{
    public string RequiredPermission => Permissions.Roles.ManagePermissions;
}
