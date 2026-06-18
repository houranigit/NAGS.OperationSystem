using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using Identity.Domain.Authorization;

namespace Identity.Application.Commands.AssignRole;

public sealed record AssignRoleCommand(Guid UserId, Guid RoleId) : ICommand, IRequirePermission
{
    public string RequiredPermission => Permissions.Users.AssignRoles;
}
