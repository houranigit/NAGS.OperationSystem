using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Features.Mobile.Mapping;
using Operations.Contracts.Mobile;
using Operations.Domain.Aggregates.Flight;
using Operations.Domain.Aggregates.WorkOrder;
using Operations.Domain.Enumerations;

namespace Operations.Application.Features.Flight.Queries.GetMobileFlightContext;

/// <summary>
/// Returns everything the mobile flight-actions screen needs: flight summary, the
/// caller's own under-review work order if any, the lookups (aircraft types, services),
/// and the snapshot of the flight's currently assigned crew.
/// </summary>
public sealed class GetMobileFlightContextQueryHandler(
    IOperationsDbContext db,
    IFlightRepository flights,
    IWorkOrderRepository workOrders,
    IAircraftTypeReader aircraftTypeReader,
    IServiceReader serviceReader)
    : IQueryHandler<GetMobileFlightContextQuery, MobileFlightContextDto>
{
    public async Task<Result<MobileFlightContextDto>> Handle(
        GetMobileFlightContextQuery request,
        CancellationToken cancellationToken)
    {
        if (request.FlightId == Guid.Empty)
            return Error.Validation("Flight id is required.");
        if (request.EmployeeId == Guid.Empty)
            return Error.Validation("Employee id is required.");

        var flight = await flights.GetByIdAsync(FlightId.From(request.FlightId), cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.");

        // Mobile-side authorisation: the caller must be on the assigned-employee list.
        // The portal can still see every flight, but the mobile API only returns context
        // for flights the user is actually rostered on.
        if (flight.AssignedEmployees.All(a => a.Employee.EmployeeId != request.EmployeeId))
            return Error.NotFound("Flight not found.");

        // Caller's own under-review work order on this flight (latest first). Approved
        // work orders are deliberately not exposed — once an approval is in place the
        // flight is settled.
        var myWorkOrderEntity = await db.WorkOrders
            .Where(w => w.FlightId == flight.Id
                        && w.CreatedByEmployeeId == request.EmployeeId
                        && w.Status == WorkOrderStatus.UnderReview)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => w.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var otherWorkOrdersExist = await db.WorkOrders.AnyAsync(
            w => w.FlightId == flight.Id
                 && (w.CreatedByEmployeeId == null || w.CreatedByEmployeeId != request.EmployeeId)
                 && w.Status != WorkOrderStatus.Deleting,
            cancellationToken);

        MobileMyWorkOrderDto? myWorkOrderDto = null;
        if (myWorkOrderEntity is not null)
        {
            var wo = await workOrders.GetByIdAsync(myWorkOrderEntity, cancellationToken);
            myWorkOrderDto = MobileMyWorkOrderDtoMapper.ToDto(wo);
        }

        var aircraftTypes = await aircraftTypeReader.ListActiveAsync(cancellationToken);
        // Work orders cannot bill AOG, so we exclude the AOG seed row at the SQL layer —
        // matching the same rule applied by the mobile lookups handler.
        var services = await serviceReader.ListActiveAsync(excludeAog: true, cancellationToken);

        var flightDetail = new MobileFlightDetailDto(
            flight.Id.Value,
            flight.FlightNumber.Value,
            flight.Customer.Name,
            flight.Customer.IataCode,
            flight.Customer.CustomerId,
            flight.Station.StationId,
            flight.Station.IataCode,
            flight.OperationType.Name,
            flight.OperationType.OperationTypeId,
            flight.Schedule.Sta,
            flight.Schedule.Std,
            flight.AircraftType?.AircraftTypeId,
            flight.AircraftType?.Model,
            flight.Status,
            flight.CanceledAt,
            flight.AcceptedWorkOrder is not null);

        return new MobileFlightContextDto(
            flightDetail,
            myWorkOrderDto,
            otherWorkOrdersExist,
            aircraftTypes,
            services,
            flight.AssignedEmployees.Select(a => a.Employee).ToList());
    }
}
