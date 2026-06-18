using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Features.Flight.Mobile;
using Operations.Application.Features.Mobile.Mapping;
using Operations.Contracts.Mobile;
using Operations.Domain.Aggregates.WorkOrder;
using Operations.Domain.Enumerations;

namespace Operations.Application.Features.Flight.Queries.GetMyAssignedFlightsForMobile;

/// <summary>
/// Pagination golden-reference style: stay on <see cref="IQueryable{T}"/> until one final
/// materialization. Filters by assigned employee + rolling time window, then projects to
/// <see cref="MobileFlightSummaryDto"/>. The "my work order" payload is batch-loaded after
/// the page query resolves work-order ids via correlated subqueries.
/// </summary>
public sealed class GetMyAssignedFlightsForMobileQueryHandler(
    IOperationsDbContext db,
    IWorkOrderRepository workOrders)
    : IQueryHandler<GetMyAssignedFlightsForMobileQuery, PaginatedResult<MobileFlightSummaryDto>>
{
    private sealed record AssignedFlightPageRow(
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

    public async Task<Result<PaginatedResult<MobileFlightSummaryDto>>> Handle(
        GetMyAssignedFlightsForMobileQuery request,
        CancellationToken cancellationToken)
    {
        if (request.EmployeeId == Guid.Empty)
            return Error.Validation("Employee id is required.");

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var search = request.Search?.Trim();
        var employeeId = request.EmployeeId;
        var now = DateTimeOffset.UtcNow;

        var query = db.Flights
            .Where(f => f.AssignedEmployees.Any(a => a.Employee.EmployeeId == employeeId))
            .Where(f => f.Status == FlightStatus.Scheduled || f.Status == FlightStatus.InProgress)
            .Where(f => f.OperationType.OperationTypeId != CoreSeedIds.AdHocOperationType)
            .WhereStaWithinWindow(now, request.WindowHours);

        if (!request.IncludeAog)
        {
            query = query.Where(f => !f.Services.Any(s => s.Service.IsAog));
        }

        if (request.Status is { } status)
        {
            query = query.Where(f => f.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(f =>
                f.FlightNumber.Value.Contains(search) ||
                f.Customer.Name.Contains(search) ||
                f.Customer.IataCode.Contains(search) ||
                f.Station.IataCode.Contains(search) ||
                f.OperationType.Name.Contains(search));
        }

        var total = await query.CountAsync(cancellationToken);

        var pageRows = await query
            .OrderBy(f => f.Schedule.Sta)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new AssignedFlightPageRow(
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
            .Select(r => new MobileFlightSummaryDto(
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
                r.OtherWorkOrdersExist,
                r.Services,
                r.AssignedEmployees))
            .ToList();

        return new PaginatedResult<MobileFlightSummaryDto>(items, total, page, pageSize);
    }
}
