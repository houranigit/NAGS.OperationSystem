using BuildingBlocks.Domain.ValueObjects;

namespace Store.Domain.Aggregates.Material;

public sealed class MaterialId : ValueObject
{
    public Guid Value { get; }

    private MaterialId(Guid value) => Value = value;

    public static MaterialId New() => new(Guid.NewGuid());

    public static MaterialId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("MaterialId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
