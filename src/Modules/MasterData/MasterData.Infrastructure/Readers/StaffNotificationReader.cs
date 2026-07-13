using MasterData.Contracts.Readers;
using MasterData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Infrastructure.Readers;

public sealed class StaffNotificationReader(MasterDataDbContext db) : IStaffNotificationReader
{
    public Task<StaffNotificationRecipient?> GetStaffRecipientAsync(Guid staffMemberId, CancellationToken cancellationToken) =>
        db.StaffMembers.AsNoTracking()
            .Where(s => s.Id == staffMemberId)
            .Select(s => new StaffNotificationRecipient(s.Id, s.FullName, s.LinkedUserId))
            .FirstOrDefaultAsync(cancellationToken);
}
