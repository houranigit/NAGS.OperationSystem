using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.ValueObjects;

public sealed class EmploymentContract : ValueObject
{
    public DateOnly From { get; }
    public DateOnly? To { get; }

    private EmploymentContract(DateOnly from, DateOnly? to)
    {
        From = from;
        To = to;
    }

    public static Result<EmploymentContract> Create(DateOnly from, DateOnly? to = null)
    {
        if (to.HasValue && to.Value < from)
            return Error.Validation("Contract end date must be on or after the start date.");

        return new EmploymentContract(from, to);
    }

    public bool IsActive(DateOnly asOf) =>
        From <= asOf && (!To.HasValue || To.Value >= asOf);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return From;
        yield return To ?? DateOnly.MinValue;
    }
}
