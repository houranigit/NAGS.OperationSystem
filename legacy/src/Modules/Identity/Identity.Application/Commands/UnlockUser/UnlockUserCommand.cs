using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using Identity.Domain.Authorization;

namespace Identity.Application.Commands.UnlockUser;

public sealed record UnlockUserCommand(Guid UserId) : ICommand, IRequirePermission
{
    public string RequiredPermission => Permissions.Users.Unlock;
}
