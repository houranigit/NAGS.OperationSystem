using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.Aggregates.Customer;

public sealed class CustomerContactId : ValueObject
{
    public Guid Value { get; }

    private CustomerContactId(Guid value) => Value = value;

    public static CustomerContactId New() => new(Guid.NewGuid());

    public static CustomerContactId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("CustomerContactId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
