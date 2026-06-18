using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Identity.Domain.Aggregates.User;
using Identity.Domain.Aggregates.UserSession;
using Identity.Domain.Authorization;

namespace Identity.Application.Commands.RevokeAllSessions;

public sealed class RevokeAllSessionsCommandHandler(
    IUserSessionRepository sessionRepository,
    ICurrentUserService currentUserService)
    : ICommandHandler<RevokeAllSessionsCommand>
{
    public async Task<Result> Handle(
        RevokeAllSessionsCommand command,
        CancellationToken cancellationToken)
    {
        var isOwner = currentUserService.UserId == command.UserId;
        var hasPermission = currentUserService.HasPermission(Permissions.Sessions.Revoke);

        if (!isOwner && !hasPermission)
            return Error.Unauthorized("You do not have permission to revoke sessions for this user.");

        var userId = UserId.From(command.UserId);
        var sessions = await sessionRepository.GetActiveByUserIdAsync(userId, cancellationToken);

        foreach (var session in sessions)
        {
            var revokeResult = session.Revoke("All sessions revoked");
            if (!revokeResult.IsSuccess)
                return revokeResult.Error;
            sessionRepository.Update(session);
        }

        return Result.Success();
    }
}
