using Identity.Contracts;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Notifications;

public sealed class WorkOrderEmailRecipientReader(IdentityDbContext db) : IWorkOrderEmailRecipientReader
{
    public Task<WorkOrderEmailRecipient?> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.Status == UserStatus.Active && !u.LoginEmailReleased)
            .Select(u => new WorkOrderEmailRecipient(
                u.Id, u.Email.Value, u.DisplayName, u.ReceiveWorkOrderSubmissionEmails))
            .FirstOrDefaultAsync(cancellationToken);
}
