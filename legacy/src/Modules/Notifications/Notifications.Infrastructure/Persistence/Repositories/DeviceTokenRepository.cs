using Microsoft.EntityFrameworkCore;
using Notifications.Domain.Aggregates.DeviceToken;

namespace Notifications.Infrastructure.Persistence.Repositories;

internal sealed class DeviceTokenRepository(NotificationsDbContext db) : IDeviceTokenRepository
{
    public Task<DeviceToken?> GetByUserAndTokenAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken = default) =>
        db.DeviceTokenItems.FirstOrDefaultAsync(
            t => t.UserId == userId && t.Token == token,
            cancellationToken);

    public async Task<IReadOnlyList<DeviceToken>> GetActiveByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await db.DeviceTokenItems
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

    public void Add(DeviceToken deviceToken) => db.DeviceTokenItems.Add(deviceToken);
    public void Update(DeviceToken deviceToken) => db.DeviceTokenItems.Update(deviceToken);
}
