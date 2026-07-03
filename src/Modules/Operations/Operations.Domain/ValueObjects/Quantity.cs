using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>A positive quantity of a consumed resource. No stock deduction (Inventory is a future module).</summary>
public sealed class Quantity : ValueObject
{
    private Quantity() { }

    private Quantity(decimal value) => Value = value;

    public decimal Value { get; private set; }

    public static Result<Quantity> Create(decimal value)
    {
        if (value <= 0)
            return Error.Validation("Quantity must be greater than zero.", "Operations.Quantity.Invalid");

        return new Quantity(value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
