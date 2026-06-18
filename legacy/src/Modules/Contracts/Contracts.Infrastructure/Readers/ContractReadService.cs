using Contracts.Contracts.Readers;
using Contracts.Domain.Enumerations;
using Contracts.Infrastructure.Persistence;
using Core.Contracts.Features.Service;
using Microsoft.EntityFrameworkCore;

namespace Contracts.Infrastructure.Readers;

internal sealed class ContractReadService(ContractsDbContext context) : IContractReadService
{
    public async Task<FindContractResult> FindActiveContractForFlightAsync(
        Guid customerId,
        Guid stationId,
        Guid operationTypeId,
        DateTimeOffset staUtc,
        CancellationToken cancellationToken = default)
    {
        // Pre-filter on indexable columns (status / customer / period). Stations and OTs are
        // child collections — translated as EF subquery checks against the child rows.
        var candidates = await context.Contracts
            .AsNoTracking()
            .Where(c => c.CustomerId == customerId)
            .Where(c => c.Status == ContractStatus.Active || c.Status == ContractStatus.Draft || c.Status == ContractStatus.Suspended)
            .Where(c => c.Period.StartDate <= staUtc && c.Period.ExpiryDate >= staUtc)
            .Where(c => c.Stations.Any(s => s.Station.StationId == stationId))
            .Where(c => c.OperationTypes.Any(o => o.OperationType.OperationTypeId == operationTypeId))
            .Select(c => new
            {
                ContractId = c.Id.Value,
                ContractNumber = c.ContractNo.Value,
                CurrencyId = c.CurrencyId,
                c.DebriefRequired,
                Status = c.Status,
                OperationType = c.OperationTypes
                    .Where(o => o.OperationType.OperationTypeId == operationTypeId)
                    .Select(o => new
                    {
                        Services = o.Services
                            .Select(s => new ServiceSnapshot(s.ServiceId, s.Name, s.IsAog))
                            .ToList()
                    })
                    .First()
            })
            .ToListAsync(cancellationToken);

        // Only Active contracts can serve as a billing target. We still surface
        // Draft/Suspended candidates as "ambiguous" if they happen to overlap, so the user
        // gets a clearer error than "not found" when the cause is contract status.
        var active = candidates.Where(c => c.Status == ContractStatus.Active).ToList();

        if (active.Count == 1)
        {
            var match = active[0];
            return new FindContractResult(
                FindContractOutcome.Found,
                new ActiveContractInfo(
                    match.ContractId,
                    match.ContractNumber,
                    match.CurrencyId,
                    match.DebriefRequired,
                    match.OperationType.Services));
        }

        if (active.Count > 1)
            return new FindContractResult(FindContractOutcome.Ambiguous, null);

        return new FindContractResult(FindContractOutcome.NotFound, null);
    }
}
