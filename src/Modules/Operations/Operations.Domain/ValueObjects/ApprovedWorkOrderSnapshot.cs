using BuildingBlocks.Domain.ValueObjects;
using Operations.Domain.Enumerations;

namespace Operations.Domain.ValueObjects;

/// <summary>
/// The scalar values captured onto a <see cref="Flights.Flight"/> when a work order is approved.
/// The flight is the billing-ready source of truth: it holds these scalars plus the reference to
/// the locked approved work order (<see cref="WorkOrderId"/>), from which the collection-heavy data
/// (actual service lines and tasks) is read. Cleared when the approval is returned/reverted.
/// </summary>
public sealed class ApprovedWorkOrderSnapshot : ValueObject
{
    private ApprovedWorkOrderSnapshot() { }

    public ApprovedWorkOrderSnapshot(
        Guid workOrderId,
        string workOrderNumber,
        WorkOrderType workOrderType,
        string actualFlightNumber,
        Guid? actualAircraftTypeId,
        string? actualAircraftTypeManufacturer,
        string? actualAircraftTypeModel,
        string? aircraftTailNumber,
        DateTimeOffset? actualArrivalUtc,
        DateTimeOffset? actualDepartureUtc,
        string? remarks,
        string? customerSignatureReference,
        Guid? canceledByUserId,
        DateTimeOffset? canceledAtUtc,
        string? cancellationReason,
        Guid approvedByUserId,
        DateTimeOffset approvedAtUtc)
    {
        WorkOrderId = workOrderId;
        WorkOrderNumber = workOrderNumber;
        WorkOrderType = workOrderType;
        ActualFlightNumber = actualFlightNumber;
        ActualAircraftTypeId = actualAircraftTypeId;
        ActualAircraftTypeManufacturer = actualAircraftTypeManufacturer;
        ActualAircraftTypeModel = actualAircraftTypeModel;
        AircraftTailNumber = aircraftTailNumber;
        ActualArrivalUtc = actualArrivalUtc;
        ActualDepartureUtc = actualDepartureUtc;
        Remarks = remarks;
        CustomerSignatureReference = customerSignatureReference;
        CanceledByUserId = canceledByUserId;
        CanceledAtUtc = canceledAtUtc;
        CancellationReason = cancellationReason;
        ApprovedByUserId = approvedByUserId;
        ApprovedAtUtc = approvedAtUtc;
    }

    public Guid WorkOrderId { get; private set; }
    public string WorkOrderNumber { get; private set; } = null!;
    public WorkOrderType WorkOrderType { get; private set; }
    public string ActualFlightNumber { get; private set; } = null!;
    public Guid? ActualAircraftTypeId { get; private set; }
    public string? ActualAircraftTypeManufacturer { get; private set; }
    public string? ActualAircraftTypeModel { get; private set; }
    public string? AircraftTailNumber { get; private set; }
    public DateTimeOffset? ActualArrivalUtc { get; private set; }
    public DateTimeOffset? ActualDepartureUtc { get; private set; }
    public string? Remarks { get; private set; }
    public string? CustomerSignatureReference { get; private set; }
    public Guid? CanceledByUserId { get; private set; }
    public DateTimeOffset? CanceledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public Guid ApprovedByUserId { get; private set; }
    public DateTimeOffset ApprovedAtUtc { get; private set; }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return WorkOrderId;
        yield return WorkOrderNumber;
        yield return WorkOrderType;
        yield return ActualFlightNumber;
        yield return ActualAircraftTypeId;
        yield return ActualAircraftTypeManufacturer;
        yield return ActualAircraftTypeModel;
        yield return AircraftTailNumber;
        yield return ActualArrivalUtc;
        yield return ActualDepartureUtc;
        yield return Remarks;
        yield return CustomerSignatureReference;
        yield return CanceledByUserId;
        yield return CanceledAtUtc;
        yield return CancellationReason;
        yield return ApprovedByUserId;
        yield return ApprovedAtUtc;
    }
}
