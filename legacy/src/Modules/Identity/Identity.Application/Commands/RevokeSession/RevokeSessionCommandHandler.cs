using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Identity.Domain.Aggregates.User;
using Identity.Domain.Aggregates.UserSession;
using Identity.Domain.Authorization;

namespace Identity.Application.Commands.RevokeSession;

public sealed class RevokeSessionCommandHandler(
    IUserSessionRepository sessionRepository,
    ICurrentUserService currentUserService)
    : ICommandHandler<RevokeSessionCommand>
{
    public async Task<Result> Handle(
        RevokeSessionCommand command,
        CancellationToken cancellationToken)
    {
        // Authorization: caller must be the session owner or have Sessions.Revoke
        var isOwner = currentUserService.UserId == command.UserId;
        var hasPermission = currentUserService.HasPermission(Permissions.Sessions.Revoke);

        if (!isOwner && !hasPermission)
            return Error.Unauthorized("You do not have permission to revoke this session.");

        var sessionId = UserSessionId.From(command.SessionId);
        var session = await sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null)
            return Error.NotFound("Session not found.");

        // Verify the session belongs to the specified user
        if (session.UserId != UserId.From(command.UserId))
            return Error.Unauthorized("Session does not belong to the specified user.");

        var result = session.Revoke("Revoked by user");
        if (!result.IsSuccess)
            return result.Error;

        sessionRepository.Update(session);
        return Result.Success();
    }
}
