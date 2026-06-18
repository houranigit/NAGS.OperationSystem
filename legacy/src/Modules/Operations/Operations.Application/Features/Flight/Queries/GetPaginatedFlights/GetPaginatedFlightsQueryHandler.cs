using System.Linq.Dynamic.Core;
using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.Customer;
using Core.Contracts.Features.Employee;
using Core.Contracts.Features.ManpowerType;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Features.Service;
using Core.Contracts.Features.Station;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Contracts.Flight;
using Operations.Contracts.WorkOrder;

namespace Operations.Application.Features.Flight.Queries.GetPaginatedFlights;

/// <summary>
/// Paginated flight grid query. Mirrors the Core.Application golden reference
/// <c>GetPaginatedCustomersQueryHandler</c>: stay on <see cref="IQueryable{T}"/> until one
/// final materialization (filter → count → order → page → project → <c>ToListAsync</c>).
/// </summary>
/// <remarks>
/// <para><b>Wrong:</b> <c>ToListAsync()</c> on the full <c>DbSet</c> (optionally with <c>Include</c>s), then paging the resulting list — that materializes every flight and all its children for every request.</para>
/// <para><b>Right:</b> keep the root query as <c>IQueryable&lt;Flight&gt;</c>, apply <see cref="GetPaginatedFlightsQuery.FilterQuery"/> / <see cref="GetPaginatedFlightsQuery.OrderByQuery"/> against entity property names, then project to <see cref="FlightDto"/> inside <c>Select</c> (nested collections navigate through <c>f.AssignedEmployees</c>, same style as <c>c.Contacts</c> in the Customer reference).</para>
/// </remarks>
public sealed class GetPaginatedFlightsQueryHandler(IOperationsDbContext db)
    : IQueryHandler<GetPaginatedFlightsQuery, PaginatedResult<FlightDto>>
{
    public async Task<Result<PaginatedResult<FlightDto>>> Handle(
        GetPaginatedFlightsQuery request,
        CancellationToken cancellationToken)
    {
        // 1. Root query — no up-front ToListAsync. EF will translate the whole pipeline to SQL.
        var query = db.Flights.AsQueryable();

        // 2. Dynamic filters — apply to the entity shape (filter strings use entity property names, including owned VOs e.g. Customer.Name, Schedule.Sta).
        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        // 3. Total count before paging.
        var total = query.Count();

        // 4. Sort — caller OrderByQuery or default scheduled arrival.
        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Schedule.Sta);

        // 5–7. Page in the database and project to FlightDto; single ToListAsync at the end.
        // AttachedWorkOrders is enriched via a correlated subquery on WorkOrders.FlightId so
        // the grid's expanded row template can render every work order linked to a flight.
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(f => new FlightDto(
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
                f.CanceledAt,
                f.AcceptedWorkOrder == null
                    ? null
                    : new WorkOrderSnapshot(f.AcceptedWorkOrder.WorkOrderId.Value, f.AcceptedWorkOrder.WorkOrderNumber.Value),
                f.AssignedEmployees
                    .Select(a => new EmployeeSnapshot(
                        a.Employee.EmployeeId,
                        a.Employee.FullName,
                        new StationSnapshot(
                            a.Employee.StationSnapshot.StationId,
                            a.Employee.StationSnapshot.Name,
                            a.Employee.StationSnapshot.IataCode),
                        new ManpowerTypeSnapshot(
                            a.Employee.ManpowerTypeSnapshot.ManpowerTypeId,
                            a.Employee.ManpowerTypeSnapshot.Name)))
                    .ToList(),
                f.Services
                    .Select(s => new ServiceSnapshot(s.Service.ServiceId, s.Service.Name, s.Service.IsAog))
                    .ToList(),
                db.WorkOrders
                    .Where(w => w.FlightId != null && w.FlightId == f.Id)
                    .OrderByDescending(w => w.CreatedAt)
                    .Select(w => new WorkOrderLightDto(
                        w.Id.Value,
                        w.WorkOrderNo == null ? null : w.WorkOrderNo.Value,
                        new FlightSnapshot(f.Id.Value, f.FlightNumber.Value),
                        new CustomerSnapshot(w.Customer.CustomerId, w.Customer.IataCode, w.Customer.Name),
                        new StationSnapshot(w.Station.StationId, w.Station.Name, w.Station.IataCode),
                        new OperationTypeSnapshot(w.OperationType.OperationTypeId, w.OperationType.Name),
                        w.AircraftType == null
                            ? null
                            : new AircraftTypeSnapshot(w.AircraftType.AircraftTypeId, w.AircraftType.Model),
                        w.AircraftTailNumber,
                        w.FlightNumber.Value,
                        w.Schedule.Sta,
                        w.Schedule.Std,
                        w.TimesActual == null ? (DateTimeOffset?)null : w.TimesActual.Ata,
                        w.TimesActual == null ? (DateTimeOffset?)null : w.TimesActual.Atd,
                        w.IsCanceled,
                        w.CanceledAt,
                        w.Status,
                        w.MarkedForDeletionAt))
                    .ToList(),
                f.CreatedAt,
                f.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<FlightDto>(items, total, request.Page, request.PageSize);
    }
}
