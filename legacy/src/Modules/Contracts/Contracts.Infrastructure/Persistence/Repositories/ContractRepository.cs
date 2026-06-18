using Contracts.Domain.Aggregates.Contract;
using Contracts.Domain.Enumerations;
using Microsoft.EntityFrameworkCore;
using ContractAggregate = Contracts.Domain.Aggregates.Contract.Contract;

namespace Contracts.Infrastructure.Persistence.Repositories;

public sealed class ContractRepository(ContractsDbContext context) : IContractRepository
{
    public async Task<ContractAggregate?> GetByIdAsync(ContractId id, CancellationToken cancellationToken = default) =>
        await context.Contracts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<ContractAggregate?> GetByIdWithDetailsAsync(
        ContractId id,
        CancellationToken cancellationToken = default) =>
        await context.Contracts
            .Include(c => c.Stations)
            .Include(c => c.OperationTypes).ThenInclude(o => o.Services)
            .Include(c => c.Services).ThenInclude(s => s.Brackets)
            .Include(c => c.Manpowers).ThenInclude(m => m.Brackets)
            .Include(c => c.Tools).ThenInclude(t => t.Brackets)
            .Include(c => c.Materials).ThenInclude(m => m.Brackets)
            .Include(c => c.GeneralSupports).ThenInclude(g => g.Brackets)
            .Include(c => c.CancellationBrackets)
            .Include(c => c.DelayBrackets)
            .Include(c => c.AdvancePayments)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<bool> ExistsByContractNoAsync(
        string contractNo,
        ContractId? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = contractNo.Trim().ToUpperInvariant();
        return await context.Contracts.AnyAsync(
            c => c.ContractNo.Value == normalized && (excludeId == null || c.Id != excludeId),
            cancellationToken);
    }

    public async Task<bool> HasActiveOverlapAsync(
        Guid customerId,
        IReadOnlyCollection<Guid> operationTypeIds,
        IReadOnlyCollection<Guid> stationIds,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        ContractId? excludeContractId,
        CancellationToken cancellationToken = default)
    {
        if (operationTypeIds.Count == 0 || stationIds.Count == 0)
            return false;

        var stationSet = stationIds.ToHashSet();
        var operationTypeSet = operationTypeIds.ToHashSet();

        // Non-terminal = anything except Terminated. Expired ones can never overlap because their
        // expiry date must be < periodStart for the input window to overlap.
        var query = context.Contracts
            .Where(c => c.CustomerId == customerId)
            .Where(c => c.Status != ContractStatus.Terminated)
            .Where(c => c.Period.StartDate < periodEnd && c.Period.ExpiryDate > periodStart);

        if (excludeContractId is not null)
            query = query.Where(c => c.Id != excludeContractId);

        // Overlap on any station ∩ any operation type. We project ids to client memory because
        // child collections are small per contract.
        var candidates = await query
            .Select(c => new
            {
                StationIds = c.Stations.Select(s => s.Station.StationId).ToList(),
                OperationTypeIds = c.OperationTypes.Select(o => o.OperationType.OperationTypeId).ToList()
            })
            .ToListAsync(cancellationToken);

        return candidates.Any(c =>
            c.StationIds.Any(stationSet.Contains) &&
            c.OperationTypeIds.Any(operationTypeSet.Contains));
    }

    public async Task<IReadOnlyList<ContractAggregate>> GetStatusSyncCandidatesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        await context.Contracts
            .Where(c => c.Status != ContractStatus.Suspended && c.Status != ContractStatus.Terminated)
            .Where(c =>
                (c.Status == ContractStatus.Draft && c.Period.StartDate <= now) ||
                (c.Status == ContractStatus.Active && c.Period.ExpiryDate < now) ||
                (c.Status == ContractStatus.Active && c.Period.StartDate > now) ||
                (c.Status == ContractStatus.Draft && c.Period.ExpiryDate < now))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ContractAggregate>> GetExpiringSoonAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // EF Core can't translate the period helper directly; pre-filter by the obvious bounds
        // and re-check the alert window in memory using the aggregate's helper.
        var candidates = await context.Contracts
            .Where(c => c.Status == ContractStatus.Active)
            .Where(c => c.Period.ExpiryAlertDays > 0)
            .Where(c => c.Period.ExpiryDate > now)
            .ToListAsync(cancellationToken);

        return candidates.Where(c => c.Period.IsInAlertWindow(now)).ToList();
    }

    public void Add(ContractAggregate contract) => context.Contracts.Add(contract);
    public void Update(ContractAggregate contract) => context.Contracts.Update(contract);
}
