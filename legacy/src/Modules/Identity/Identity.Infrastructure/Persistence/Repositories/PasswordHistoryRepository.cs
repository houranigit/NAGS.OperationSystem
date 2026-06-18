using Identity.Domain.Aggregates.User;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence.Repositories;

public sealed class PasswordHistoryRepository(IdentityDbContext context) : IPasswordHistoryRepository
{
    public async Task<IReadOnlyList<PasswordHistoryEntry>> GetLastNAsync(UserId userId, int count, CancellationToken ct = default) =>
        await context.PasswordHistory
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(count)
            .ToListAsync(ct);

    public void Add(PasswordHistoryEntry entry) => context.PasswordHistory.Add(entry);
}
