using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>The actual departure information supplied in an employee's submission.</summary>
public sealed class DepartureDetails : ValueObject
{
    private DepartureDetails() { }

    public DateTimeOffset Atd { get; private set; }
    public CustomerSignature? CustomerSignature { get; private set; }
    public StaffMemberSnapshot HandledByEmployee { get; private set; } = null!;
    public string? Remarks { get; private set; }

    public static Result<DepartureDetails> Create(
        DateTimeOffset atd,
        CustomerSignature? customerSignature,
        StaffMemberSnapshot? handledByEmployee,
        string? remarks)
    {
        if (atd == default)
            return Error.Validation("Actual departure time is required.", "Operations.DepartureDetails.AtdRequired");
        if (handledByEmployee is null || handledByEmployee.StaffMemberId == Guid.Empty)
            return Error.Validation("Departure handling employee is required.", "Operations.DepartureDetails.HandledByEmployeeRequired");

        return new DepartureDetails
        {
            Atd = atd.ToUniversalTime(),
            CustomerSignature = customerSignature?.Copy(),
            HandledByEmployee = new StaffMemberSnapshot(handledByEmployee.StaffMemberId, handledByEmployee.FullName, handledByEmployee.EmployeeId),
            Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim()
        };
    }

    // Each submission owns its value instances independently in persistence.
    internal DepartureDetails Copy() => Create(Atd, CustomerSignature, HandledByEmployee, Remarks).Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Atd;
        yield return CustomerSignature;
        yield return HandledByEmployee;
        yield return Remarks;
    }
}
