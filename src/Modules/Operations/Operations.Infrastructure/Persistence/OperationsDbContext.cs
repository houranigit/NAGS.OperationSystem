using System.Reflection;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Operations.Application.Abstractions;
using Operations.Domain.Flights;

namespace Operations.Infrastructure.Persistence;

public sealed class OperationsDbContext(DbContextOptions<OperationsDbContext> options)
    : DbContext(options), IOperationsDbContext, IOutboxDbContext
{
    public const string Schema = "operations";

    public DbSet<Flight> Flights => Set<Flight>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] rowVersion) where TEntity : class =>
        Entry(entity).Property("RowVersion").OriginalValue = rowVersion;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureSubmissionConcurrencyChecks();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureSubmissionConcurrencyChecks();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnsureSubmissionConcurrencyChecks()
    {
        ChangeTracker.DetectChanges();
        var serviceEntries = ChangeTracker.Entries<FlightService>().ToList();
        var changedServiceIds = serviceEntries.Where(HasChangedOwnedGraph).Select(entry => entry.Entity.Id).ToHashSet();
        foreach (var performer in ChangeTracker.Entries<FlightServicePerformer>().Where(HasChangedOwnedGraph))
            changedServiceIds.Add(performer.Entity.FlightServiceId);
        foreach (var attachment in ChangeTracker.Entries<FlightServiceAttachment>().Where(HasChangedOwnedGraph))
            changedServiceIds.Add(attachment.Entity.FlightServiceId);
        // Include deleted children, which are no longer present in the service's collections.
        var changedServiceFlightIds = serviceEntries.Where(entry => changedServiceIds.Contains(entry.Entity.Id))
            .Select(entry => entry.Entity.FlightId).ToHashSet();

        foreach (var flight in ChangeTracker.Entries<Flight>()
            .Where(entry => entry.State is EntityState.Unchanged or EntityState.Modified).ToList())
        {
            if (flight.Entity.ArrivalSubmissions.Any(submission => HasChangedOwnedGraph(Entry(submission)))
                || flight.Entity.DepartureSubmissions.Any(submission => HasChangedOwnedGraph(Entry(submission)))
                || flight.Entity.CancellationSubmissions.Any(submission => HasChangedOwnedGraph(Entry(submission)))
                || changedServiceFlightIds.Contains(flight.Entity.Id))
            {
                // Force the flight rowversion check even when the supplied timestamp is unchanged.
                flight.Property(item => item.UpdatedAtUtc).IsModified = true;
            }
        }
    }

    private static bool HasChangedOwnedGraph(EntityEntry entry) =>
        entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
        || entry.References.Any(reference => reference.Metadata.TargetEntityType.IsOwned()
            && reference.TargetEntry is { } dependent && HasChangedOwnedGraph(dependent));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        modelBuilder.ApplyOutboxInbox();
        base.OnModelCreating(modelBuilder);
    }
}
