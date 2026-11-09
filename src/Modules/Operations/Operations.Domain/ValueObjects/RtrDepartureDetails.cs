using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>The optional departure record that closes a return-to-ramp occurrence.</summary>
public sealed class RtrDepartureDetails : ValueObject
{
    private RtrDepartureDetails() { }

    public DateTimeOffset DepartedAtUtc { get; private set; }
    public RtrCustomerSignature? CustomerSignature { get; private set; }
    public StaffMemberSnapshot HandledByEmployee { get; private set; } = null!;
    public string? Remarks { get; private set; }

    public static Result<RtrDepartureDetails> Create(DateTimeOffset departedAtUtc,
        RtrCustomerSignature? customerSignature, StaffMemberSnapshot? handledByEmployee, string? remarks)
    {
        if (departedAtUtc == default)
            return Error.Validation("Return-to-ramp departure time is required.", "Operations.RtrDepartureDetails.DepartedAtRequired");
        if (handledByEmployee is null || handledByEmployee.StaffMemberId == Guid.Empty)
            return Error.Validation("Departure handling employee is required.", "Operations.RtrDepartureDetails.HandledByEmployeeRequired");
        if (remarks?.Trim().Length > 2000)
            return Error.Validation("Departure remarks must be at most 2000 characters.", "Operations.RtrDepartureDetails.RemarksTooLong");

        return new RtrDepartureDetails
        {
            DepartedAtUtc = departedAtUtc.ToUniversalTime(),
            CustomerSignature = customerSignature?.Copy(),
            HandledByEmployee = new StaffMemberSnapshot(handledByEmployee.StaffMemberId,
                handledByEmployee.FullName, handledByEmployee.EmployeeId),
            Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim()
        };
    }

    internal RtrDepartureDetails Copy() => Create(DepartedAtUtc, CustomerSignature, HandledByEmployee, Remarks).Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return DepartedAtUtc;
        yield return CustomerSignature;
        yield return HandledByEmployee;
        yield return Remarks;
    }
}
