using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;
using Contracts.Domain.Enumerations;

namespace Contracts.Domain.ValueObjects;

/// <summary>
/// Contract-level fee or discount. <see cref="FeeType.Fixed"/> is expressed in the contract
/// currency; <see cref="FeeType.Percentage"/> is 0–100 and applies proportionally to the
/// operation amount. <see cref="Value"/> = 0 means "no charge" — required fields always
/// exist but may be harmless.
/// </summary>
public sealed class Fee : ValueObject
{
    public FeeType Type { get; private set; }
    public decimal Value { get; private set; }

    private Fee() { }

    private Fee(FeeType type, decimal value)
    {
        Type = type;
        Value = value;
    }

    public static Result<Fee> Create(FeeType type, decimal value)
    {
        if (!Enum.IsDefined(type))
            return Error.Validation($"Unknown fee type '{type}'.");

        if (value < 0m)
            return Error.Validation("Fee value cannot be negative.");

        if (type == FeeType.Percentage && value > 100m)
            return Error.Validation("Percentage fee cannot exceed 100.");

        return new Fee(type, decimal.Round(value, 4, MidpointRounding.AwayFromZero));
    }

    public static Fee FixedZero { get; } = new(FeeType.Fixed, 0m);

    public bool IsZero => Value == 0m;

    public override string ToString() =>
        Type == FeeType.Percentage ? $"{Value}%" : Value.ToString("0.####");

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Type;
        yield return Value;
    }
}
