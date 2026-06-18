using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.Customer;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Features.Station;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Contracts.Flight;

namespace Operations.Application.Features.Flight.Queries.GetPaginatedFlightLights;

/// <summary>
/// Paginated lightweight flight list for schedulers and dropdowns. Pipeline mirrors the
/// Core.Application golden reference <c>GetPaginatedCustomersQueryHandler</c> and its
/// <c>*SelectOptions</c> sibling: IQueryable → filter → count → order → paginate → project → single <c>ToListAsync</c>.
/// </summary>
/// <remarks>
/// Child collections (assigned employees, attached work orders) are not needed for the light shape, so the projection
/// stays on the root entity and its owned value objects — the database does the paging, and only the page we need crosses the wire.
/// </remarks>
public sealed class GetPaginatedFlightLightsQueryHandler(IOperationsDbContext db)
    : IQueryHandler<GetPaginatedFlightLightsQuery, PaginatedResult<FlightLightDto>>
{
    public async Task<Result<PaginatedResult<FlightLightDto>>> Handle(
        GetPaginatedFlightLightsQuery request,
        CancellationToken cancellationToken)
    {
        // 1. Root query — stay on IQueryable until the final ToListAsync.
        var query = db.Flights.AsQueryable();

        // 2. Dynamic grid-style filters (entity property names, e.g. Customer.Name, Schedule.Sta).
        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        // 3. Total before paging.
        var total = query.Count();

        // 4. Sort — caller OrderByQuery or default scheduled arrival.
        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Schedule.Sta);

        // 5–7. Page in the database, map to the light Contracts row, then materialize once.
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
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

        return new PaginatedResult<FlightLightDto>(items, total, request.Page, request.PageSize);
    }
}
