using BuildingBlocks.Application.Messaging;
using Microsoft.EntityFrameworkCore;
using Operations.Domain.Flights;
using Operations.Domain.WorkOrders;

namespace Operations.Application.Abstractions;

/// <summary>
/// Application-facing view of the Operations persistence store, implemented by the module DbContext
/// in Infrastructure. Extends <see cref="IOutboxDbContext"/> so handlers can dedupe via the inbox and
/// enqueue integration events in the same transaction as their state change.
/// </summary>
public interface IOperationsDbContext : IOutboxDbContext
{
    public DbSet<Flight> Flights { get; }

    public DbSet<FlightTimelineEntry> FlightTimelineEntries { get; }

    public DbSet<WorkOrder> WorkOrders { get; }

    public DbSet<WorkOrderTimelineEntry> WorkOrderTimelineEntries { get; }

    /// <summary>Sets the original concurrency token so a stale update fails with a concurrency conflict.</summary>
    public void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] rowVersion) where TEntity : class;
}
