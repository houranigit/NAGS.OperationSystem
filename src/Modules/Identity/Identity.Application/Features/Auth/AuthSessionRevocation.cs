using Identity.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Auth;

internal static class AuthSessionRevocation
{
    public static async Task RevokeActiveSessionsAsync(
        IIdentityDbContext db,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sessions = await db.Sessions
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
            session.Revoke(now);
    }
}
