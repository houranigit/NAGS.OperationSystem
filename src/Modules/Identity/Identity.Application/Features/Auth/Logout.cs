using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Auth;

public sealed record LogoutCommand(string? RefreshToken) : ICommand;

public sealed class LogoutCommandHandler(
    IIdentityDbContext db,
    ITokenService tokenService,
    TimeProvider timeProvider)
    : ICommandHandler<LogoutCommand>
{
    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return Result.Success();

        var hash = tokenService.HashRefreshToken(request.RefreshToken);
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.RefreshTokenHash == hash, cancellationToken);
        if (session is not null)
        {
            session.Revoke(timeProvider.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}
