using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.Aggregates.Customer;

public sealed class CustomerId : ValueObject
{
    public Guid Value { get; }

    private CustomerId(Guid value) => Value = value;

    public static CustomerId New() => new(Guid.NewGuid());

    public static CustomerId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("CustomerId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
