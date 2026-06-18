using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Infrastructure.Persistence;
using BuildingBlocks.Infrastructure.Persistence.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Store.Application.Abstractions;
using Store.Domain.Aggregates.GeneralSupport;
using Store.Domain.Aggregates.GeneralSupportPricePlan;
using Store.Domain.Aggregates.Material;
using Store.Domain.Aggregates.MaterialPricePlan;
using Store.Domain.Aggregates.Tool;
using Store.Domain.Aggregates.ToolPricePlan;
using Store.Domain.Aggregates.Unit;
using Unit = Store.Domain.Aggregates.Unit.Unit;

namespace Store.Infrastructure.Persistence;

public sealed class StoreDbContext(
    DbContextOptions<StoreDbContext> options,
    ICurrentUserService currentUserService,
    IPublisher publisher)
    : BaseDbContext(options, currentUserService, publisher), IStoreDbContext
{
    protected override string SchemaName => "store";

    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Tool> Tools => Set<Tool>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<GeneralSupport> GeneralSupports => Set<GeneralSupport>();
    public DbSet<ToolPricePlan> ToolPricePlans => Set<ToolPricePlan>();
    public DbSet<MaterialPricePlan> MaterialPricePlans => Set<MaterialPricePlan>();
    public DbSet<GeneralSupportPricePlan> GeneralSupportPricePlans => Set<GeneralSupportPricePlan>();

    IQueryable<Unit> IStoreDbContext.Units => Units;
    IQueryable<Tool> IStoreDbContext.Tools => Tools;
    IQueryable<Material> IStoreDbContext.Materials => Materials;
    IQueryable<GeneralSupport> IStoreDbContext.GeneralSupports => GeneralSupports;
    IQueryable<ToolPricePlan> IStoreDbContext.ToolPricePlans => ToolPricePlans;
    IQueryable<MaterialPricePlan> IStoreDbContext.MaterialPricePlans => MaterialPricePlans;
    IQueryable<GeneralSupportPricePlan> IStoreDbContext.GeneralSupportPricePlans => GeneralSupportPricePlans;

    public async Task<bool> IsAlreadyProcessedAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        await InboxMessages.AnyAsync(m => m.Id == eventId, cancellationToken);

    public void MarkProcessed(Guid eventId, string eventType)
    {
        InboxMessages.Add(new InboxMessage
        {
            Id = eventId,
            Type = eventType,
            ReceivedAt = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow
        });
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoreDbContext).Assembly);
    }
}
