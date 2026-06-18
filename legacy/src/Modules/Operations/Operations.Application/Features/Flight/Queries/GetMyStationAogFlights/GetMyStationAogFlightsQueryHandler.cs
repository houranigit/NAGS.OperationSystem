using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Readers;
using Core.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Features.Flight.Mobile;
using Operations.Application.Features.Mobile.Mapping;
using Operations.Contracts.Mobile;
using Operations.Domain.Aggregates.WorkOrder;
using Operations.Domain.Enumerations;

namespace Operations.Application.Features.Flight.Queries.GetMyStationAogFlights;

/// <summary>
/// Looks up the calling employee's station via <see cref="IEmployeeReader"/>, then returns
/// every Scheduled / InProgress flight at that station whose <c>Services</c> collection
/// contains an AOG service. Any station employee can open these flights and start a work
/// order (after explicit "Claim flight" — handled by <c>ClaimAogFlightCommand</c>).
/// </summary>
public sealed class GetMyStationAogFlightsQueryHandler(
    IOperationsDbContext db,
    IEmployeeReader employeeReader,
    IWorkOrderRepository workOrders)
    : IQueryHandler<GetMyStationAogFlightsQuery, IReadOnlyList<MobileAogFlightSummaryDto>>
{
    private sealed record AogFlightPageRow(
        Guid Id,
        string FlightNumber,
        string CustomerName,
        string CustomerIataCode,
        string StationCode,
        string OperationTypeCode,
        DateTimeOffset Sta,
        DateTimeOffset Std,
        string? AircraftModel,
        FlightStatus Status,
        DateTimeOffset? CanceledAt,
        int AssignedEmployeesCount,
        Guid? MyWorkOrderId,
        bool OtherWorkOrdersExist);

    public async Task<Result<IReadOnlyList<MobileAogFlightSummaryDto>>> Handle(
        GetMyStationAogFlightsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.EmployeeId == Guid.Empty)
            return Error.Validation("Employee id is required.");

        var employee = await employeeReader.GetByIdAsync(request.EmployeeId, cancellationToken);
        if (employee is null)
            return Error.NotFound("Employee not found.");

        var stationId = employee.StationSnapshot.StationId;
        var employeeId = request.EmployeeId;
        var now = DateTimeOffset.UtcNow;

        var pageRows = await db.Flights
            .Where(f => f.Station.StationId == stationId)
            .Where(f => f.Status == FlightStatus.Scheduled || f.Status == FlightStatus.InProgress)
            .Where(f => f.OperationType.OperationTypeId != CoreSeedIds.AdHocOperationType)
            .Where(f => f.Services.Any(s => s.Service.IsAog))
            .WhereStaWithinWindow(now, request.WindowHours)
            .OrderBy(f => f.Schedule.Sta)
            .Select(f => new AogFlightPageRow(
                f.Id.Value,
                f.FlightNumber.Value,
                f.Customer.Name,
                f.Customer.IataCode,
                f.Station.IataCode,
                f.OperationType.Name,
                f.Schedule.Sta,
                f.Schedule.Std,
                f.AircraftType == null ? null : f.AircraftType.Model,
                f.Status,
                f.CanceledAt,
                f.AssignedEmployees.Count(),
                db.WorkOrders
                    .Where(w => w.FlightId == f.Id
                                && w.CreatedByEmployeeId == employeeId
                                && w.Status == WorkOrderStatus.UnderReview)
                    .OrderByDescending(w => w.CreatedAt)
                    .Select(w => (Guid?)w.Id.Value)
                    .FirstOrDefault(),
                db.WorkOrders.Any(w =>
                    w.FlightId == f.Id
                    && (w.CreatedByEmployeeId == null || w.CreatedByEmployeeId != employeeId)
                    && w.Status != WorkOrderStatus.Deleting)))
            .ToListAsync(cancellationToken);

        var woIds = pageRows
            .Select(r => r.MyWorkOrderId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var loaded = await workOrders.GetByIdsAsync(woIds, cancellationToken);
        var woById = loaded.ToDictionary(w => w.Id.Value, w => MobileMyWorkOrderDtoMapper.ToDto(w)!);

        var items = pageRows
            .Select(r => new MobileAogFlightSummaryDto(
                r.Id,
                r.FlightNumber,
                r.CustomerName,
                r.CustomerIataCode,
                r.StationCode,
                r.OperationTypeCode,
                r.Sta,
                r.Std,
                r.AircraftModel,
                r.Status,
                r.CanceledAt,
                r.AssignedEmployeesCount,
                r.MyWorkOrderId is { } wid && woById.TryGetValue(wid, out var dto) ? dto : null,
                r.OtherWorkOrdersExist))
            .ToList();

        return Result<IReadOnlyList<MobileAogFlightSummaryDto>>.Success(items);
    }
}
