using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Infrastructure.Persistence;
using BuildingBlocks.Infrastructure.Persistence.Models;
using Contracts.Application.Abstractions;
using Contracts.Domain.Aggregates.Contract;
using Contracts.Domain.Aggregates.Contract.Pricing;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ContractAggregate = Contracts.Domain.Aggregates.Contract.Contract;

namespace Contracts.Infrastructure.Persistence;

public sealed class ContractsDbContext(
    DbContextOptions<ContractsDbContext> options,
    ICurrentUserService currentUserService,
    IPublisher publisher)
    : BaseDbContext(options, currentUserService, publisher), IContractsDbContext
{
    protected override string SchemaName => "contracts";

    public DbSet<ContractAggregate> Contracts => Set<ContractAggregate>();
    public DbSet<ContractStation> ContractStations => Set<ContractStation>();
    public DbSet<ContractOperationType> ContractOperationTypes => Set<ContractOperationType>();
    public DbSet<ContractService> ContractServices => Set<ContractService>();
    public DbSet<ContractManpower> ContractManpowers => Set<ContractManpower>();
    public DbSet<ContractTool> ContractTools => Set<ContractTool>();
    public DbSet<ContractMaterial> ContractMaterials => Set<ContractMaterial>();
    public DbSet<ContractGeneralSupport> ContractGeneralSupports => Set<ContractGeneralSupport>();
    public DbSet<CancellationBracket> CancellationBrackets => Set<CancellationBracket>();
    public DbSet<DelayBracket> DelayBrackets => Set<DelayBracket>();

    IQueryable<ContractAggregate> IContractsDbContext.Contracts => Contracts;
    IQueryable<ContractStation> IContractsDbContext.ContractStations => ContractStations;
    IQueryable<ContractOperationType> IContractsDbContext.ContractOperationTypes => ContractOperationTypes;
    IQueryable<ContractService> IContractsDbContext.ContractServices => ContractServices;
    IQueryable<ContractManpower> IContractsDbContext.ContractManpowers => ContractManpowers;
    IQueryable<ContractTool> IContractsDbContext.ContractTools => ContractTools;
    IQueryable<ContractMaterial> IContractsDbContext.ContractMaterials => ContractMaterials;
    IQueryable<ContractGeneralSupport> IContractsDbContext.ContractGeneralSupports => ContractGeneralSupports;
    IQueryable<CancellationBracket> IContractsDbContext.CancellationBrackets => CancellationBrackets;
    IQueryable<DelayBracket> IContractsDbContext.DelayBrackets => DelayBrackets;

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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ContractsDbContext).Assembly);
    }
}
