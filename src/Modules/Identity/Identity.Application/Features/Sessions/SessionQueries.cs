using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Identity.Application.Contracts;
using Identity.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Sessions;

internal static class SessionMapping
{
    public static UserSessionDto ToDto(this UserSession session, DateTimeOffset now, bool isCurrent) =>
        new(
            session.Id,
            session.UserId,
            session.CreatedAtUtc,
            session.ExpiresAtUtc,
            session.RevokedAtUtc,
            session.IsActive(now),
            isCurrent,
            session.CreatedByIp,
            session.UserAgent);
}

// --- Admin: list sessions for a given user --------------------------------

public sealed record GetUserSessionsQuery(Guid UserId, bool ActiveOnly = false)
    : IQuery<IReadOnlyList<UserSessionDto>>;

public sealed class GetUserSessionsQueryHandler(IIdentityDbContext db, TimeProvider timeProvider)
    : IQueryHandler<GetUserSessionsQuery, IReadOnlyList<UserSessionDto>>
{
    public async Task<Result<IReadOnlyList<UserSessionDto>>> Handle(GetUserSessionsQuery request, CancellationToken cancellationToken)
    {
        var userExists = await db.Users.AsNoTracking().AnyAsync(u => u.Id == request.UserId, cancellationToken);
        if (!userExists)
            return Error.NotFound("User not found.", "Identity.User.NotFound");

        var now = timeProvider.GetUtcNow();

        var query = db.Sessions.AsNoTracking().Where(s => s.UserId == request.UserId);
        if (request.ActiveOnly)
            query = query.Where(s => s.RevokedAtUtc == null && s.ExpiresAtUtc > now);

        var sessions = await query
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        IReadOnlyList<UserSessionDto> items = sessions.Select(s => s.ToDto(now, isCurrent: false)).ToList();
        return Result.Success(items);
    }
}

// --- Self: list my own sessions -------------------------------------------

public sealed record GetMySessionsQuery(string? CurrentRefreshToken)
    : IQuery<IReadOnlyList<UserSessionDto>>;

public sealed class GetMySessionsQueryHandler(
    IIdentityDbContext db,
    ICurrentUser currentUser,
    ITokenService tokenService,
    TimeProvider timeProvider)
    : IQueryHandler<GetMySessionsQuery, IReadOnlyList<UserSessionDto>>
{
    public async Task<Result<IReadOnlyList<UserSessionDto>>> Handle(GetMySessionsQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Error.Unauthorized();

        var now = timeProvider.GetUtcNow();
        var currentHash = string.IsNullOrWhiteSpace(request.CurrentRefreshToken)
            ? null
            : tokenService.HashRefreshToken(request.CurrentRefreshToken);

        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        IReadOnlyList<UserSessionDto> items = sessions
            .Select(s => s.ToDto(now, isCurrent: currentHash is not null && s.RefreshTokenHash == currentHash))
            .ToList();
        return Result.Success(items);
    }
}
