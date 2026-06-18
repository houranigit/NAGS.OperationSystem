using BuildingBlocks.Domain.ValueObjects;

namespace Store.Domain.Aggregates.GeneralSupport;

public sealed class GeneralSupportId : ValueObject
{
    public Guid Value { get; }

    private GeneralSupportId(Guid value) => Value = value;

    public static GeneralSupportId New() => new(Guid.NewGuid());

    public static GeneralSupportId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("GeneralSupportId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
