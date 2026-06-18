using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.Aggregates.Employee;

public sealed class EmployeeId : ValueObject
{
    public Guid Value { get; }

    private EmployeeId(Guid value) => Value = value;

    public static EmployeeId New() => new(Guid.NewGuid());

    public static EmployeeId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("EmployeeId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
