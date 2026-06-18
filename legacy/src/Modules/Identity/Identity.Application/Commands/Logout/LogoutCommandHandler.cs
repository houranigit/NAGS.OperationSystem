using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Identity.Domain.Aggregates.UserSession;

namespace Identity.Application.Commands.Logout;

public sealed class LogoutCommandHandler(
    IUserSessionRepository sessionRepository)
    : ICommandHandler<LogoutCommand>
{
    public async Task<Result> Handle(
        LogoutCommand command,
        CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetByRefreshTokenAsync(command.RefreshToken, cancellationToken);
        if (session is null)
            return Error.NotFound("Session not found.");

        var revokeResult = session.Revoke("User logged out");
        if (!revokeResult.IsSuccess)
            return revokeResult.Error;

        sessionRepository.Update(session);
        return Result.Success();
    }
}
