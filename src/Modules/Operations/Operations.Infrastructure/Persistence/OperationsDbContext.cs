using System.Reflection;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Domain.Flights;
using Operations.Domain.Mobile;
using Operations.Domain.WorkOrders;
using Operations.Infrastructure.BackgroundJobs;

namespace Operations.Infrastructure.Persistence;

public sealed class OperationsDbContext(DbContextOptions<OperationsDbContext> options)
    : DbContext(options), IOperationsDbContext, IOutboxDbContext
{
    public const string Schema = "operations";

    public DbSet<Flight> Flights => Set<Flight>();
    public DbSet<FlightTimelineEntry> FlightTimelineEntries => Set<FlightTimelineEntry>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderServiceLine> WorkOrderServiceLines => Set<WorkOrderServiceLine>();
    public DbSet<WorkOrderTimelineEntry> WorkOrderTimelineEntries => Set<WorkOrderTimelineEntry>();
    public DbSet<MobileMutation> MobileMutations => Set<MobileMutation>();
    public DbSet<FlightReminderSchedule> FlightReminderSchedules => Set<FlightReminderSchedule>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] rowVersion) where TEntity : class =>
        Entry(entity).Property("RowVersion").OriginalValue = rowVersion;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        modelBuilder.ApplyOutboxInbox();
        base.OnModelCreating(modelBuilder);
    }
}
