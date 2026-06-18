using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using Identity.Domain.Authorization;

namespace Identity.Application.Commands.InviteUser;

public sealed record InviteUserCommand(
    string Username,
    string Email,
    int UserTypeId,
    Guid? ExternalReferenceId,
    IReadOnlyList<Guid>? RoleIds = null
) : ICommand<InviteUserResult>, IRequirePermission
{
    public string RequiredPermission => Permissions.Users.Invite;
}

public sealed record InviteUserResult(Guid UserId, string InvitationToken);
