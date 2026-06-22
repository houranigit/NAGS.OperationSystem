using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Application.Messaging;

/// <summary>
/// Implemented by every module DbContext that participates in transactional messaging. Exposes the
/// module-owned outbox and inbox tables so command/event handlers can enqueue integration events in
/// the same transaction as their state change, and the outbox processor can drain them.
/// </summary>
public interface IOutboxDbContext
{
    public DbSet<OutboxMessage> OutboxMessages { get; }
    public DbSet<InboxMessage> InboxMessages { get; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
