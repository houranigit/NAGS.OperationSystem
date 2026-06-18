using BuildingBlocks.Domain.ValueObjects;

namespace Store.Domain.Aggregates.Tool;

public sealed class EquipmentId : ValueObject
{
    public Guid Value { get; }

    private EquipmentId(Guid value) => Value = value;

    public static EquipmentId New() => new(Guid.NewGuid());

    public static EquipmentId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("EquipmentId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
