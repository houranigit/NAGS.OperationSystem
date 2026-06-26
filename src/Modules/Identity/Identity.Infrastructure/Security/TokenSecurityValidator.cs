using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Identity.Infrastructure.Security;

/// <summary>
/// Bounded live validation of an access token. Positive results are cached briefly to avoid adding
/// remote database round trips to every API call while still bounding revocation propagation.
/// </summary>
public sealed class TokenSecurityValidator(IIdentityDbContext db, TimeProvider timeProvider, IMemoryCache cache)
    : ITokenSecurityValidator
{
    private static readonly TimeSpan PositiveCacheTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromSeconds(5);

    public async Task<bool> IsCurrentAsync(
        Guid userId,
        string? securityStamp,
        Guid? sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(securityStamp, out var stamp))
            return false;

        var cacheKey = $"identity:token-current:{userId:N}:{stamp:N}:{sessionId?.ToString("N") ?? "none"}";
        if (cache.TryGetValue(cacheKey, out bool cached))
            return cached;

        var isCurrent = await QueryIsCurrentAsync(userId, stamp, sessionId, cancellationToken);
        cache.Set(cacheKey, isCurrent, isCurrent ? PositiveCacheTtl : NegativeCacheTtl);
        return isCurrent;
    }

    private async Task<bool> QueryIsCurrentAsync(
        Guid userId,
        Guid stamp,
        Guid? sessionId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var currentUser = db.Users.AsNoTracking()
            .Where(u =>
                u.Id == userId
                && u.Status == UserStatus.Active
                && u.SecurityStamp == stamp
                && (u.LockoutEndUtc == null || u.LockoutEndUtc <= now));

        if (sessionId is { } id)
        {
            return await currentUser.AnyAsync(u =>
                db.Sessions.AsNoTracking().Any(s =>
                    s.Id == id
                    && s.UserId == u.Id
                    && s.RevokedAtUtc == null
                    && s.ExpiresAtUtc > now), cancellationToken);
        }

        return await currentUser.AnyAsync(cancellationToken);
    }
}
