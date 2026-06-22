using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace MasterData.Domain.StaffMembers;

/// <summary>
/// An optional employment period for a staff member. When present, a start date is required and the
/// end date (if any) cannot precede the start date.
/// </summary>
public sealed class EmploymentContract : ValueObject
{
    private EmploymentContract(DateOnly startDate, DateOnly? endDate)
    {
        StartDate = startDate;
        EndDate = endDate;
    }

    public DateOnly StartDate { get; }
    public DateOnly? EndDate { get; }

    public static Result<EmploymentContract> Create(DateOnly startDate, DateOnly? endDate)
    {
        if (endDate is { } end && end < startDate)
            return Error.Validation("The employment end date cannot precede the start date.", "MasterData.EmploymentContract.EndBeforeStart");

        return new EmploymentContract(startDate, endDate);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return StartDate;
        yield return EndDate;
    }
}
