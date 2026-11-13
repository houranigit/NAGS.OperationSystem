using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>The actual arrival information supplied in an employee's submission.</summary>
public sealed class ArrivalDetails : ValueObject
{
    private ArrivalDetails() { }

    public string? CustomerWorkReferenceNumber { get; private set; }
    public DateTimeOffset Ata { get; private set; }
    public AircraftTypeSnapshot AircraftType { get; private set; } = null!;
    public string AircraftTailNumber { get; private set; } = null!;
    public StaffMemberSnapshot ReceivedByEmployee { get; private set; } = null!;
    public string? Remarks { get; private set; }

    public static Result<ArrivalDetails> Create(
        string? customerWorkReferenceNumber,
        DateTimeOffset ata,
        AircraftTypeSnapshot? aircraftType,
        string? aircraftTailNumber,
        StaffMemberSnapshot? receivedByEmployee,
        string? remarks)
    {
        if (ata == default)
            return Error.Validation("Actual arrival time is required.", "Operations.ArrivalDetails.AtaRequired");
        if (aircraftType is null || aircraftType.AircraftTypeId == Guid.Empty)
            return Error.Validation("Actual aircraft type is required.", "Operations.ArrivalDetails.AircraftTypeRequired");
        if (string.IsNullOrWhiteSpace(aircraftTailNumber))
            return Error.Validation("Aircraft tail number is required.", "Operations.ArrivalDetails.AircraftTailNumberRequired");
        if (receivedByEmployee is null || receivedByEmployee.StaffMemberId == Guid.Empty)
            return Error.Validation("Receiving employee is required.", "Operations.ArrivalDetails.ReceivedByEmployeeRequired");

        return new ArrivalDetails
        {
            CustomerWorkReferenceNumber = string.IsNullOrWhiteSpace(customerWorkReferenceNumber) ? null : customerWorkReferenceNumber.Trim().ToUpperInvariant(),
            Ata = ata.ToUniversalTime(),
            AircraftType = new AircraftTypeSnapshot(aircraftType.AircraftTypeId, aircraftType.Manufacturer, aircraftType.Model),
            AircraftTailNumber = aircraftTailNumber.Trim().ToUpperInvariant(),
            ReceivedByEmployee = new StaffMemberSnapshot(receivedByEmployee.StaffMemberId, receivedByEmployee.FullName, receivedByEmployee.EmployeeId),
            Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim()
        };
    }

    // Owned value instances cannot be shared across submissions in persistence.
    internal ArrivalDetails Copy() => Create(CustomerWorkReferenceNumber, Ata, AircraftType,
        AircraftTailNumber, ReceivedByEmployee, Remarks).Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CustomerWorkReferenceNumber;
        yield return Ata;
        yield return AircraftType;
        yield return AircraftTailNumber;
        yield return ReceivedByEmployee;
        yield return Remarks;
    }
}
