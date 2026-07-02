using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
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

public sealed record GetUserSessionsQuery(Guid UserId, int Page = 1, int PageSize = 20, bool ActiveOnly = false)
    : IQuery<PagedResult<UserSessionDto>>;

public sealed class GetUserSessionsQueryHandler(IIdentityDbContext db, TimeProvider timeProvider)
    : IQueryHandler<GetUserSessionsQuery, PagedResult<UserSessionDto>>
{
    public async Task<Result<PagedResult<UserSessionDto>>> Handle(GetUserSessionsQuery request, CancellationToken cancellationToken)
    {
        var paging = PageRequest.From(request.Page, request.PageSize);
        var now = timeProvider.GetUtcNow();

        var query = db.Sessions.AsNoTracking().Where(s => s.UserId == request.UserId);
        if (request.ActiveOnly)
            query = query.Where(s => s.RevokedAtUtc == null && s.ExpiresAtUtc > now);

        var total = await query.LongCountAsync(cancellationToken);
        if (total == 0)
        {
            var userExists = await db.Users.AsNoTracking().AnyAsync(u => u.Id == request.UserId, cancellationToken);
            if (!userExists)
                return Error.NotFound("User not found.", "Identity.User.NotFound");
        }
        if (paging.IsOutOfRange(total))
            return paging.Empty<UserSessionDto>(total);

        var sessions = await query
            .OrderByDescending(s => s.CreatedAtUtc)
            .ThenByDescending(s => s.Id)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .ToListAsync(cancellationToken);

        IReadOnlyList<UserSessionDto> items = sessions.Select(s => s.ToDto(now, isCurrent: false)).ToList();
        return paging.ToResult<UserSessionDto>(items, total);
    }
}

// --- Self: list my own sessions -------------------------------------------

public sealed record GetMySessionsQuery(string? CurrentRefreshToken, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<UserSessionDto>>;

public sealed class GetMySessionsQueryHandler(
    IIdentityDbContext db,
    ICurrentUser currentUser,
    ITokenService tokenService,
    TimeProvider timeProvider)
    : IQueryHandler<GetMySessionsQuery, PagedResult<UserSessionDto>>
{
    public async Task<Result<PagedResult<UserSessionDto>>> Handle(GetMySessionsQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Error.Unauthorized();

        var paging = PageRequest.From(request.Page, request.PageSize);
        var now = timeProvider.GetUtcNow();
        var currentHash = string.IsNullOrWhiteSpace(request.CurrentRefreshToken)
            ? null
            : tokenService.HashRefreshToken(request.CurrentRefreshToken);

        var query = db.Sessions.AsNoTracking().Where(s => s.UserId == userId);
        var total = await query.LongCountAsync(cancellationToken);
        if (paging.IsOutOfRange(total))
            return paging.Empty<UserSessionDto>(total);

        var sessions = await query
            .OrderByDescending(s => s.CreatedAtUtc)
            .ThenByDescending(s => s.Id)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .ToListAsync(cancellationToken);

        IReadOnlyList<UserSessionDto> items = sessions
            .Select(s => s.ToDto(now, isCurrent: currentHash is not null && s.RefreshTokenHash == currentHash))
            .ToList();
        return paging.ToResult<UserSessionDto>(items, total);
    }
}
