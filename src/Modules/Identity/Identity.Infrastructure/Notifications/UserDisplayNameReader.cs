using Identity.Contracts;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Notifications;

public sealed class UserDisplayNameReader(IdentityDbContext db) : IUserDisplayNameReader
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        var names = new Dictionary<Guid, string>();
        // Keep SQL parameter sets bounded without one query per creator or exposing account data.
        foreach (var batch in userIds.Where(id => id != Guid.Empty).Distinct().Chunk(500))
        {
            var found = await db.Users.AsNoTracking().Where(user => batch.Contains(user.Id))
                .Select(user => new { user.Id, user.DisplayName })
                .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);
            foreach (var item in found) names[item.Key] = item.Value;
        }
        return names;
    }
}
