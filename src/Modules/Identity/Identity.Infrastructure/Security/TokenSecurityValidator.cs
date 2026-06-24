using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Security;

/// <summary>
/// Live validation of an access token. Fails closed: any missing/inactive user, stale security
/// stamp, or revoked/expired session rejects the token.
/// </summary>
public sealed class TokenSecurityValidator(IIdentityDbContext db, TimeProvider timeProvider)
    : ITokenSecurityValidator
{
    public async Task<bool> IsCurrentAsync(
        Guid userId,
        string? securityStamp,
        Guid? sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(securityStamp, out var stamp))
            return false;

        var now = timeProvider.GetUtcNow();

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null || user.Status != UserStatus.Active || user.IsLockedOut(now))
            return false;

        if (user.SecurityStamp != stamp)
            return false;

        if (sessionId is { } id)
        {
            var session = await db.Sessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

            if (session is null || !session.IsActive(now))
                return false;
        }

        return true;
    }
}
