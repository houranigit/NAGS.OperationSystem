using Audit.Domain.Trails;
using BuildingBlocks.Application.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Audit.Application.Abstractions;

/// <summary>
/// Application-facing view of the Audit store. Extends <see cref="IOutboxDbContext"/> so the
/// audit-event consumer can dedupe via the module inbox while persisting trails idempotently.
/// </summary>
public interface IAuditDbContext : IOutboxDbContext
{
    public DbSet<AuditTrail> AuditTrails { get; }
}
