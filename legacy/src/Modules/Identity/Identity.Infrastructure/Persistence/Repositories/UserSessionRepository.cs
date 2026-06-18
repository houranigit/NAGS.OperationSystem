using Identity.Domain.Aggregates.User;
using Identity.Domain.Aggregates.UserSession;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence.Repositories;

public sealed class UserSessionRepository(IdentityDbContext context) : IUserSessionRepository
{
    public async Task<UserSession?> GetByRefreshTokenAsync(string refreshToken, CancellationToken ct = default) =>
        await context.UserSessions.FirstOrDefaultAsync(x => x.RefreshToken == refreshToken, ct);

    public async Task<IReadOnlyList<UserSession>> GetActiveByUserIdAsync(UserId userId, CancellationToken ct = default) =>
        await context.UserSessions
            .Where(x => x.UserId == userId && x.RevokedAt == null && x.RefreshTokenExpiresAt > DateTime.UtcNow)
            .ToListAsync(ct);

    public async Task<UserSession?> GetByIdAsync(UserSessionId id, CancellationToken ct = default) =>
        await context.UserSessions.FirstOrDefaultAsync(x => x.Id == id, ct);

    public void Add(UserSession session) => context.UserSessions.Add(session);
    public void Update(UserSession session) => context.UserSessions.Update(session);
}
