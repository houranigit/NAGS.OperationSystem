using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Queries;
using Identity.Domain.Authorization;
using Operations.Contracts.Flight;
using Operations.Domain.Enumerations;

namespace Operations.Application.Features.Flight.Queries.GetFlightLightsInPeriod;

/// <summary>
/// Non-paged list of <see cref="FlightLightDto"/> overlapping the period (STA–STD window intersects <paramref name="PeriodFrom"/>–<paramref name="PeriodTo"/>).
/// Optional <paramref name="Statuses"/> narrows by lifecycle (<see langword="null"/> = no status filter).
/// Empty list narrows to nothing (no flights returned).
/// </summary>
public sealed record GetFlightLightsInPeriodQuery(
    DateTimeOffset PeriodFrom,
    DateTimeOffset PeriodTo,
    IReadOnlyList<FlightStatus>? Statuses = null) : IQuery<IReadOnlyList<FlightLightDto>>, IRequirePermission
{
    public string RequiredPermission => Permissions.Scheduler.Read;
}
