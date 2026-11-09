using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>The single arrival record for a return-to-ramp occurrence.</summary>
public sealed class RtrArrivalDetails : ValueObject
{
    private RtrArrivalDetails() { }

    public DateTimeOffset ArrivedAtUtc { get; private set; }
    public StaffMemberSnapshot ReceivedByEmployee { get; private set; } = null!;
    public string? Remarks { get; private set; }

    public static Result<RtrArrivalDetails> Create(DateTimeOffset arrivedAtUtc,
        StaffMemberSnapshot? receivedByEmployee, string? remarks)
    {
        if (arrivedAtUtc == default)
            return Error.Validation("Return-to-ramp arrival time is required.", "Operations.RtrArrivalDetails.ArrivedAtRequired");
        if (receivedByEmployee is null || receivedByEmployee.StaffMemberId == Guid.Empty)
            return Error.Validation("Receiving employee is required.", "Operations.RtrArrivalDetails.ReceivedByEmployeeRequired");
        if (remarks?.Trim().Length > 2000)
            return Error.Validation("Arrival remarks must be at most 2000 characters.", "Operations.RtrArrivalDetails.RemarksTooLong");

        return new RtrArrivalDetails
        {
            ArrivedAtUtc = arrivedAtUtc.ToUniversalTime(),
            ReceivedByEmployee = new StaffMemberSnapshot(receivedByEmployee.StaffMemberId,
                receivedByEmployee.FullName, receivedByEmployee.EmployeeId),
            Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim()
        };
    }

    internal RtrArrivalDetails Copy() => Create(ArrivedAtUtc, ReceivedByEmployee, Remarks).Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return ArrivedAtUtc;
        yield return ReceivedByEmployee;
        yield return Remarks;
    }
}
