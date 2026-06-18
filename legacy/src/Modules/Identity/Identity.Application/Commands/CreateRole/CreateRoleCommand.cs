using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using Identity.Domain.Authorization;

namespace Identity.Application.Commands.CreateRole;

public sealed record CreateRoleCommand(
    string Name,
    string? Description,
    IReadOnlyList<string> PermissionCodes
) : ICommand<CreateRoleResult>, IRequirePermission
{
    public string RequiredPermission => Permissions.Roles.Create;
}

public sealed record CreateRoleResult(Guid RoleId);
