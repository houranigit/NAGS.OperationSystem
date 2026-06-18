using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Operations.Application.Abstractions;
using Operations.Domain.Aggregates.Flight;
using Operations.Domain.Aggregates.WorkOrder;
using FlightEntity = Operations.Domain.Aggregates.Flight.Flight;
using WorkOrderEntity = Operations.Domain.Aggregates.WorkOrder.WorkOrder;

namespace Operations.Infrastructure.Persistence;

public sealed class OperationsDbContext(
    DbContextOptions<OperationsDbContext> options,
    ICurrentUserService currentUserService,
    IPublisher publisher)
    : BaseDbContext(options, currentUserService, publisher), IOperationsDbContext
{
    protected override string SchemaName => "operations";

    public DbSet<FlightEntity> Flights => Set<FlightEntity>();
    public DbSet<WorkOrderEntity> WorkOrders => Set<WorkOrderEntity>();
    public DbSet<StationWorkOrderCounter> StationWorkOrderCounters => Set<StationWorkOrderCounter>();

    IQueryable<FlightEntity> IOperationsDbContext.Flights => Flights;
    IQueryable<WorkOrderEntity> IOperationsDbContext.WorkOrders => WorkOrders;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Strongly-typed ID value objects must be treated as scalar-convertible values,
        // not auto-discovered as owned entity types. Each IEntityTypeConfiguration still
        // calls HasConversion locally for explicit column mapping.
        configurationBuilder.Properties<FlightId>()
            .HaveConversion<FlightIdConverter>();
        configurationBuilder.Properties<WorkOrderId>()
            .HaveConversion<WorkOrderIdConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OperationsDbContext).Assembly);

        // IDs are created in domain constructors (application-side), so PKs should never
        // be treated as database-generated. This avoids EF marking keys/FKs as modified
        // in HasMany + OwnsOne graphs and causing false concurrency exceptions.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var key in entity.GetKeys())
            {
                foreach (var prop in key.Properties)
                    prop.ValueGenerated = ValueGenerated.Never;
            }
        }
    }
}
