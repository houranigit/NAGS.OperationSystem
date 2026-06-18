using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Queries;
using Contracts.Contracts.Contract;
using Identity.Domain.Authorization;

namespace Contracts.Application.Features.Contract.Queries.FindContractForFlight;

/// <summary>
/// Wizard-facing query that wraps <c>IContractReadService.FindActiveContractForFlightAsync</c>
/// for the flight scheduler. Used by the Add-flight dialog when the user clicks Next on the
/// route step to verify that an active contract covers the chosen
/// (Customer, Station, OperationType, STA) tuple before we let them assign crew.
/// </summary>
public sealed record FindContractForFlightQuery(
    Guid CustomerId,
    Guid StationId,
    Guid OperationTypeId,
    DateTimeOffset Sta) : IQuery<FindContractForFlightDto>, IRequirePermission
{
    public string RequiredPermission => Permissions.Scheduler.ReadLookups;
}
