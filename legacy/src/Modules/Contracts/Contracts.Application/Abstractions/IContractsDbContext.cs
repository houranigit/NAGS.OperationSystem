using BuildingBlocks.Application.Abstractions;
using Contracts.Domain.Aggregates.Contract;
using Contracts.Domain.Aggregates.Contract.Pricing;

namespace Contracts.Application.Abstractions;

/// <summary>Abstraction implemented by Contracts infrastructure persistence.</summary>
public interface IContractsDbContext : IUnitOfWork
{
    IQueryable<Contract> Contracts { get; }
    IQueryable<ContractStation> ContractStations { get; }
    IQueryable<ContractOperationType> ContractOperationTypes { get; }
    IQueryable<ContractService> ContractServices { get; }
    IQueryable<ContractManpower> ContractManpowers { get; }
    IQueryable<ContractTool> ContractTools { get; }
    IQueryable<ContractMaterial> ContractMaterials { get; }
    IQueryable<ContractGeneralSupport> ContractGeneralSupports { get; }
    IQueryable<CancellationBracket> CancellationBrackets { get; }
    IQueryable<DelayBracket> DelayBrackets { get; }

    /// <summary>True if an inbox row already exists for the given EventId — handlers should short-circuit.</summary>
    Task<bool> IsAlreadyProcessedAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an inbox row keyed by <paramref name="eventId"/>. The next SaveChanges persists it; a
    /// re-delivery raises a unique-key conflict so the handler is naturally idempotent.
    /// </summary>
    void MarkProcessed(Guid eventId, string eventType);
}
