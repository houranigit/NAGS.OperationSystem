using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Queries;
using Identity.Domain.Authorization;
using Operations.Contracts.Flight;

namespace Operations.Application.Features.Flight.Queries.GetFlightById;

/// <summary>Loads a single flight from the store; <see langword="null"/> if not found.</summary>
public sealed record GetFlightByIdQuery(Guid Id) : IQuery<FlightDto?>, IRequirePermission
{
    public string RequiredPermission => Permissions.Flights.Read;
}
