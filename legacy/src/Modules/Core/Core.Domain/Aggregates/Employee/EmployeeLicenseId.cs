using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.Aggregates.Employee;

public sealed class EmployeeLicenseId : ValueObject
{
    public Guid Value { get; }

    private EmployeeLicenseId(Guid value) => Value = value;

    public static EmployeeLicenseId New() => new(Guid.NewGuid());

    public static EmployeeLicenseId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("EmployeeLicenseId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
