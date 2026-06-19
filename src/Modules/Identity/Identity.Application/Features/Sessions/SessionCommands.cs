using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Sessions;

// --- Admin: revoke a single session by id ---------------------------------

public sealed record RevokeSessionCommand(Guid SessionId) : ICommand;

public sealed class RevokeSessionCommandHandler(IIdentityDbContext db, TimeProvider timeProvider)
    : ICommandHandler<RevokeSessionCommand>
{
    public async Task<Result> Handle(RevokeSessionCommand request, CancellationToken cancellationToken)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken);
        if (session is null)
            return Error.NotFound("Session not found.", "Identity.Session.NotFound");

        session.Revoke(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// --- Admin: revoke all active sessions for a user -------------------------

public sealed record RevokeUserSessionsCommand(Guid UserId) : ICommand;

public sealed class RevokeUserSessionsCommandHandler(IIdentityDbContext db, TimeProvider timeProvider)
    : ICommandHandler<RevokeUserSessionsCommand>
{
    public async Task<Result> Handle(RevokeUserSessionsCommand request, CancellationToken cancellationToken)
    {
        var userExists = await db.Users.AnyAsync(u => u.Id == request.UserId, cancellationToken);
        if (!userExists)
            return Error.NotFound("User not found.", "Identity.User.NotFound");

        var now = timeProvider.GetUtcNow();
        var sessions = await db.Sessions
            .Where(s => s.UserId == request.UserId && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
            session.Revoke(now);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// --- Self: revoke one of my own sessions ----------------------------------

public sealed record RevokeMySessionCommand(Guid SessionId) : ICommand;

public sealed class RevokeMySessionCommandHandler(
    IIdentityDbContext db,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
    : ICommandHandler<RevokeMySessionCommand>
{
    public async Task<Result> Handle(RevokeMySessionCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Error.Unauthorized();

        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken);
        if (session is null || session.UserId != userId)
            return Error.NotFound("Session not found.", "Identity.Session.NotFound");

        session.Revoke(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// --- Self: revoke all my other sessions ("sign out other devices") --------

public sealed record RevokeMyOtherSessionsCommand(string? CurrentRefreshToken) : ICommand;

public sealed class RevokeMyOtherSessionsCommandHandler(
    IIdentityDbContext db,
    ICurrentUser currentUser,
    ITokenService tokenService,
    TimeProvider timeProvider)
    : ICommandHandler<RevokeMyOtherSessionsCommand>
{
    public async Task<Result> Handle(RevokeMyOtherSessionsCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Error.Unauthorized();

        var now = timeProvider.GetUtcNow();
        var currentHash = string.IsNullOrWhiteSpace(request.CurrentRefreshToken)
            ? null
            : tokenService.HashRefreshToken(request.CurrentRefreshToken);

        var sessions = await db.Sessions
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions.Where(s => currentHash is null || s.RefreshTokenHash != currentHash))
            session.Revoke(now);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
