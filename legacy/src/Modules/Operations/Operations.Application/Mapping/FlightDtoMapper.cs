using Operations.Contracts.Flight;
using Operations.Contracts.WorkOrder;
using Operations.Domain.Aggregates.Flight;
using DomainWorkOrderSnapshot = Operations.Domain.ValueObjects.WorkOrderSnapshot;
using ContractWorkOrderSnapshot = Operations.Contracts.WorkOrder.WorkOrderSnapshot;

namespace Operations.Application.Mapping;

/// <summary>
/// Maps the <see cref="Flight"/> aggregate to the outward-facing DTOs used by the
/// Host and other modules. Attached work-order details (beyond ids) are not part of
/// the aggregate; handlers enrich <see cref="FlightDto.AttachedWorkOrders"/> later if
/// that data is needed.
/// </summary>
public static class FlightDtoMapper
{
    public static FlightDto FromAggregate(Flight flight)
    {
        var assigned = flight.AssignedEmployees
            .Select(a => a.Employee)
            .ToList();

        var services = flight.Services
            .Select(s => s.Service)
            .ToList();

        return new FlightDto(
            Id: flight.Id.Value,
            ContractId: flight.ContractId,
            ContractNumber: flight.ContractNumber,
            CustomerSnapshot: flight.Customer,
            StationSnapshot: flight.Station,
            OperationTypeSnapshot: flight.OperationType,
            AircraftTypeId: flight.AircraftType,
            FlightNumber: flight.FlightNumber.Value,
            Sta: flight.Schedule.Sta,
            Std: flight.Schedule.Std,
            Status: flight.Status,
            CanceledAt: flight.CanceledAt,
            AcceptedWorkOrderSnapshot: ToContract(flight.AcceptedWorkOrder),
            AssignedEmployees: assigned,
            Services: services,
            AttachedWorkOrders: Array.Empty<WorkOrderLightDto>(),
            CreatedAt: flight.CreatedAt,
            UpdatedAt: flight.UpdatedAt);
    }

    public static FlightLightDto ToLight(FlightDto dto) => new(
        Id: dto.Id,
        ContractId: dto.ContractId,
        ContractNumber: dto.ContractNumber,
        CustomerSnapshot: dto.CustomerSnapshot,
        StationSnapshot: dto.StationSnapshot,
        OperationTypeSnapshot: dto.OperationTypeSnapshot,
        AircraftTypeId: dto.AircraftTypeId,
        FlightNumber: dto.FlightNumber,
        Sta: dto.Sta,
        Std: dto.Std,
        Status: dto.Status,
        CanceledAt: dto.CanceledAt);

    public static FlightLightDto ToLight(Flight flight) => ToLight(FromAggregate(flight));

    public static FlightSnapshot ToFlightSnapshot(Flight flight) =>
        new(flight.Id.Value, flight.FlightNumber.Value);

    private static ContractWorkOrderSnapshot? ToContract(DomainWorkOrderSnapshot? snapshot) =>
        snapshot is null
            ? null
            : new ContractWorkOrderSnapshot(snapshot.WorkOrderId.Value, snapshot.WorkOrderNumber.Value);
}
