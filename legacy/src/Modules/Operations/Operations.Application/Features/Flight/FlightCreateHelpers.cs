using BuildingBlocks.Domain.Results;
using Contracts.Contracts.Readers;
using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.Customer;
using Core.Contracts.Features.Employee;
using Core.Contracts.Features.ManpowerType;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Features.Service;
using Core.Contracts.Features.Station;
using Core.Contracts.Seeding;
using Operations.Domain.Enumerations;
using Operations.Domain.ValueObjects;
using DomainFlight = Operations.Domain.Aggregates.Flight.Flight;

namespace Operations.Application.Features.Flight;

/// <summary>
/// Shared create validation for single and batch commands.
/// New flights always start as <see cref="FlightStatus.Scheduled"/> with no work order linkage; other workflows own status and WOs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the snapshots are cloned:</b> EF Core owned types must have a unique owner. When <c>BatchCreateFlightsCommandHandler</c>
/// fans out the same UI-built <see cref="CustomerSnapshot"/> / <see cref="EmployeeSnapshot"/> reference across N flights (and
/// thousands of <c>FlightAssignedEmployee</c> rows), EF attaches the owned row to one parent and writes <c>NULL</c> columns
/// (e.g. <c>EmployeeManpowerTypeName</c>) into the rest. Cloning here guarantees every aggregate receives its own owned graph
/// regardless of what the caller passed.
/// </para>
/// </remarks>
internal static class FlightCreateHelpers
{
    /// <summary>
    /// Resolves a contract for the (Customer, Station, OT, STA) tuple, then asks
    /// <see cref="DomainFlight.Create"/> to build a Scheduled flight bound to it. Rejects
    /// the AdHoc OT (only mobile create-from-scratch may use that), surfaces the contract
    /// resolver's "not found" / "ambiguous" outcomes as validation/conflict errors.
    /// Crew assignment is optional — flights may be created without assignees and rostered later.
    /// </summary>
    internal static async Task<Result<DomainFlight>> TryCreateScheduledFlightAsync(
        CustomerSnapshot customerSnapshot,
        StationSnapshot stationSnapshot,
        OperationTypeSnapshot operationTypeSnapshot,
        AircraftTypeSnapshot? aircraftTypeId,
        string flightNumber,
        DateTimeOffset sta,
        DateTimeOffset std,
        IReadOnlyList<EmployeeSnapshot> assignedEmployees,
        IContractReadService contracts,
        CancellationToken cancellationToken)
    {
        if (operationTypeSnapshot.OperationTypeId == CoreSeedIds.AdHocOperationType)
            return Result<DomainFlight>.Failure(Error.Validation(
                "Ad Hoc operation type is not allowed for scheduled flights."));

        var number = FlightNumber.Create(flightNumber);
        if (number.IsFailure)
            return Result<DomainFlight>.Failure(number.Error);

        var schedule = ScheduledTime.Create(sta, std);
        if (schedule.IsFailure)
            return Result<DomainFlight>.Failure(schedule.Error);

        var resolved = await contracts.FindActiveContractForFlightAsync(
            customerSnapshot.CustomerId,
            stationSnapshot.StationId,
            operationTypeSnapshot.OperationTypeId,
            sta,
            cancellationToken);

        switch (resolved.Outcome)
        {
            case FindContractOutcome.NotFound:
                return Result<DomainFlight>.Failure(Error.Validation(
                    "No active contract covers this customer / station / operation type at the scheduled time."));
            case FindContractOutcome.Ambiguous:
                return Result<DomainFlight>.Failure(Error.Conflict(
                    "Multiple active contracts cover this slot — please clean up overlapping contracts before creating the flight."));
        }

        var contract = resolved.Contract!;
        var now = DateTimeOffset.UtcNow;

        return DomainFlight.Create(
            contract.ContractId,
            contract.ContractNumber,
            Clone(customerSnapshot),
            Clone(stationSnapshot),
            Clone(operationTypeSnapshot),
            number.Value,
            schedule.Value,
            aircraftTypeId is null ? null : Clone(aircraftTypeId),
            CloneAll(contract.OperationTypeServices),
            CloneAll(assignedEmployees),
            assignmentRequired: false,
            now);
    }

    private static CustomerSnapshot Clone(CustomerSnapshot x) =>
        new(x.CustomerId, x.IataCode, x.Name);

    private static StationSnapshot Clone(StationSnapshot x) =>
        new(x.StationId, x.Name, x.IataCode);

    private static OperationTypeSnapshot Clone(OperationTypeSnapshot x) =>
        new(x.OperationTypeId, x.Name);

    private static AircraftTypeSnapshot Clone(AircraftTypeSnapshot x) =>
        new(x.AircraftTypeId, x.Model);

    private static IReadOnlyList<ServiceSnapshot> CloneAll(IReadOnlyList<ServiceSnapshot> items)
    {
        var copies = new List<ServiceSnapshot>(items.Count);
        foreach (var s in items)
            copies.Add(new ServiceSnapshot(s.ServiceId, s.Name, s.IsAog));
        return copies;
    }

    private static IReadOnlyList<EmployeeSnapshot> CloneAll(IReadOnlyList<EmployeeSnapshot> items)
    {
        var copies = new List<EmployeeSnapshot>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var e = items[i];
            copies.Add(new EmployeeSnapshot(
                e.EmployeeId,
                e.FullName,
                new StationSnapshot(e.StationSnapshot.StationId, e.StationSnapshot.Name, e.StationSnapshot.IataCode),
                new ManpowerTypeSnapshot(e.ManpowerTypeSnapshot.ManpowerTypeId, e.ManpowerTypeSnapshot.Name)));
        }
        return copies;
    }
}
