using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using Identity.Domain.Authorization;

namespace Identity.Application.Commands.RemoveRole;

public sealed record RemoveRoleCommand(Guid UserId, Guid RoleId) : ICommand, IRequirePermission
{
    public string RequiredPermission => Permissions.Users.AssignRoles;
}
