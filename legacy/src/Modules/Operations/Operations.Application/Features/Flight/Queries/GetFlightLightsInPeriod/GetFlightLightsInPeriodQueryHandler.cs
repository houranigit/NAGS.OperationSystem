using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.Customer;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Features.Station;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Contracts.Flight;

namespace Operations.Application.Features.Flight.Queries.GetFlightLightsInPeriod;

/// <summary>
/// Non-paged scheduler feed for flights overlapping a period. Mirrors the IQueryable-only pipeline of
/// <c>GetPaginatedCustomersQueryHandler</c>: filter and order on <see cref="IQueryable{T}"/>, then project
/// to the Contracts row in one <c>Select</c> and materialize with a single <c>ToListAsync</c>.
/// </summary>
public sealed class GetFlightLightsInPeriodQueryHandler(IOperationsDbContext db)
    : IQueryHandler<GetFlightLightsInPeriodQuery, IReadOnlyList<FlightLightDto>>
{
    public async Task<Result<IReadOnlyList<FlightLightDto>>> Handle(
        GetFlightLightsInPeriodQuery request,
        CancellationToken cancellationToken)
    {
        if (request.PeriodFrom > request.PeriodTo)
        {
            return Result<IReadOnlyList<FlightLightDto>>.Failure(
                Error.Validation("Period start must not be after period end."));
        }

        // Empty explicit status list = narrow to nothing; short-circuit before touching SQL.
        if (request.Statuses is { Count: 0 })
            return Result<IReadOnlyList<FlightLightDto>>.Success(Array.Empty<FlightLightDto>());

        var from = request.PeriodFrom;
        var to = request.PeriodTo;

        // 1. Root query — stay on IQueryable until the final ToListAsync.
        var query = db.Flights
            .Where(f => f.Schedule.Sta <= to && f.Schedule.Std >= from);

        // 2. Optional status narrowing (status list null = no filter).
        if (request.Statuses is { Count: > 0 } statuses)
            query = query.Where(f => statuses.Contains(f.Status));

        // 3–5. Order, project to the light Contracts row and materialize once.
        var items = await query
            .OrderBy(f => f.Schedule.Sta)
            .Select(f => new FlightLightDto(
                f.Id.Value,
                f.ContractId,
                f.ContractNumber,
                new CustomerSnapshot(f.Customer.CustomerId, f.Customer.IataCode, f.Customer.Name),
                new StationSnapshot(f.Station.StationId, f.Station.Name, f.Station.IataCode),
                new OperationTypeSnapshot(f.OperationType.OperationTypeId, f.OperationType.Name),
                f.AircraftType == null
                    ? null
                    : new AircraftTypeSnapshot(f.AircraftType.AircraftTypeId, f.AircraftType.Model),
                f.FlightNumber.Value,
                f.Schedule.Sta,
                f.Schedule.Std,
                f.Status,
                f.CanceledAt))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<FlightLightDto>>.Success(items);
    }
}
