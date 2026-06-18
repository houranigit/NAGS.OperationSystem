using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.ValueObjects;

/// <summary>
/// Non-negative amount expressed in the parent contract's currency. Currency is implicit —
/// we never mix currencies inside a single contract.
/// </summary>
public sealed class Money : ValueObject
{
    public decimal Amount { get; private set; }

    private Money() { }

    private Money(decimal amount) => Amount = amount;

    public static Money Zero { get; } = new(0m);

    public static Result<Money> Create(decimal amount)
    {
        if (amount < 0m)
            return Error.Validation("Money amount cannot be negative.");

        return new Money(decimal.Round(amount, 4, MidpointRounding.AwayFromZero));
    }

    /// <summary>Throwing factory for internal use after invariants have already been checked.</summary>
    public static Money From(decimal amount)
    {
        var result = Create(amount);
        if (result.IsFailure)
            throw new ArgumentException(result.Error.Description, nameof(amount));
        return result.Value;
    }

    public bool IsZero => Amount == 0m;
    public bool IsPositive => Amount > 0m;

    public Money Add(Money other) => From(Amount + other.Amount);

    public Money Subtract(Money other)
    {
        if (other.Amount > Amount)
            throw new InvalidOperationException("Resulting Money would be negative.");
        return From(Amount - other.Amount);
    }

    public override string ToString() => Amount.ToString("0.####");

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Amount;
    }
}
