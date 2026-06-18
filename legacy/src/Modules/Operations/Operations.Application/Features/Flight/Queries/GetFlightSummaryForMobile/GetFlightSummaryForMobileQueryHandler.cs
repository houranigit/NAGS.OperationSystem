using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Features.Mobile.Mapping;
using Operations.Contracts.Mobile;
using Operations.Domain.Aggregates.Flight;
using Operations.Domain.Aggregates.WorkOrder;
using Operations.Domain.Enumerations;

namespace Operations.Application.Features.Flight.Queries.GetFlightSummaryForMobile;

/// <summary>
/// Identical projection shape as <c>GetMyAssignedFlightsForMobileQueryHandler</c> but
/// for a single flight id. Lives next to the list query so changes to the row shape
/// stay in lock-step; mobile uses this handler from the real-time sync apply path.
/// </summary>
public sealed class GetFlightSummaryForMobileQueryHandler(
    IOperationsDbContext db,
    IWorkOrderRepository workOrders)
    : IQueryHandler<GetFlightSummaryForMobileQuery, MobileFlightSummaryDto?>
{
    private sealed record FlightSummaryRow(
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
        bool OtherWorkOrdersExist,
        List<MobileFlightServiceDto> Services,
        List<MobileFlightAssignedEmployeeDto> AssignedEmployees);

    public async Task<Result<MobileFlightSummaryDto?>> Handle(
        GetFlightSummaryForMobileQuery request,
        CancellationToken cancellationToken)
    {
        if (request.FlightId == Guid.Empty)
            return Error.Validation("Flight id is required.");
        if (request.EmployeeId == Guid.Empty)
            return Error.Validation("Employee id is required.");

        var flightId = FlightId.From(request.FlightId);
        var employeeId = request.EmployeeId;

        var row = await db.Flights
            .Where(f => f.Id == flightId)
            .Select(f => new FlightSummaryRow(
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
                    && w.Status != WorkOrderStatus.Deleting),
                f.Services
                    .Select(s => new MobileFlightServiceDto(
                        s.Service.ServiceId,
                        s.Service.Name,
                        s.Service.IsAog))
                    .ToList(),
                f.AssignedEmployees
                    .Select(a => new MobileFlightAssignedEmployeeDto(
                        a.Employee.EmployeeId,
                        a.Employee.FullName,
                        a.Employee.ManpowerTypeSnapshot.Name))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return Result<MobileFlightSummaryDto?>.Success(null);

        MobileMyWorkOrderDto? myWo = null;
        if (row.MyWorkOrderId is { } wid)
        {
            var woEntity = await workOrders.GetByIdAsync(WorkOrderId.From(wid), cancellationToken);
            myWo = MobileMyWorkOrderDtoMapper.ToDto(woEntity);
        }

        var item = new MobileFlightSummaryDto(
            row.Id,
            row.FlightNumber,
            row.CustomerName,
            row.CustomerIataCode,
            row.StationCode,
            row.OperationTypeCode,
            row.Sta,
            row.Std,
            row.AircraftModel,
            row.Status,
            row.CanceledAt,
            row.AssignedEmployeesCount,
            myWo,
            row.OtherWorkOrdersExist,
            row.Services,
            row.AssignedEmployees);

        return Result<MobileFlightSummaryDto?>.Success(item);
    }
}
