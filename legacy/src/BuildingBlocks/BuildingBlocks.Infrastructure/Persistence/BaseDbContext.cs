using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Infrastructure.Persistence.Attributes;
using BuildingBlocks.Infrastructure.Persistence.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace BuildingBlocks.Infrastructure.Persistence;

public abstract class BaseDbContext(
    DbContextOptions options,
    ICurrentUserService currentUserService,
    IPublisher publisher)
    : DbContext(options), IUnitOfWork, IOutboxWriter
{
    public DbSet<AuditTrail> AuditTrails => Set<AuditTrail>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    // Each derived module DbContext provides its own schema name (lowercase).
    // E.g. "identity", "core", "audit"
    protected abstract string SchemaName { get; }

    // False only on AuditDbContext so one migration creates audit.AuditTrails; other modules keep it out of their migrations.
    protected virtual bool ExcludeAuditTrailsFromMigrations => true;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditTrail>(b =>
        {
            if (ExcludeAuditTrailsFromMigrations)
                b.ToTable("AuditTrails", "audit", t => t.ExcludeFromMigrations());
            else
                b.ToTable("AuditTrails", "audit");
            b.HasKey(x => x.Id);
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("OutboxMessages", SchemaName);
            b.HasKey(x => x.Id);
        });

        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("InboxMessages", SchemaName);
            b.HasKey(x => x.Id);
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        CaptureAuditTrail();
        var result = await base.SaveChangesAsync(cancellationToken);
        await DispatchDomainEventsAsync(cancellationToken);
        return result;
    }

    public void Write(string eventType, string content)
    {
        OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = eventType,
            Content = content,
            CreatedAt = DateTime.UtcNow
        });
    }

    private void CaptureAuditTrail()
    {
        var entries = ChangeTracker.Entries()
            .Where(e =>
                e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
                e.Entity.GetType().GetCustomAttributes(typeof(SkipAuditAttribute), inherit: true).Length == 0)
            .ToList();

        foreach (var entry in entries)
        {
            AuditTrails.Add(BuildAuditTrail(entry));
        }
    }

    private AuditTrail BuildAuditTrail(EntityEntry entry)
    {
        var primaryKey = entry.Properties
            .FirstOrDefault(p => p.Metadata.IsPrimaryKey())
            ?.CurrentValue
            ?.ToString() ?? string.Empty;

        return new AuditTrail
        {
            Id = Guid.NewGuid(),
            EntityName = entry.Entity.GetType().Name,
            EntityId = primaryKey,
            Action = entry.State switch
            {
                EntityState.Added => AuditAction.Add,
                EntityState.Modified => AuditAction.Modify,
                EntityState.Deleted => AuditAction.Delete,
                _ => throw new ArgumentOutOfRangeException(nameof(entry.State))
            },
            OldValues = entry.State == EntityState.Modified
                ? JsonSerializer.Serialize(entry.Properties
                    .ToDictionary(p => p.Metadata.Name, p => p.OriginalValue))
                : null,
            NewValues = entry.State != EntityState.Deleted
                ? JsonSerializer.Serialize(entry.Properties
                    .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue))
                : null,
            ChangedBy = currentUserService.UserId,
            ChangedAt = DateTime.UtcNow
        };
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        var aggregates = ChangeTracker
            .Entries<IHasDomainEvents>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        var domainEvents = aggregates
            .SelectMany(a => a.DomainEvents)
            .ToList();

        foreach (var aggregate in aggregates)
            aggregate.ClearDomainEvents();

        foreach (var domainEvent in domainEvents)
            await publisher.Publish(domainEvent, cancellationToken);
    }
}
