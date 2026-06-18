using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract;

public sealed class DelayBracketId : ValueObject
{
    public Guid Value { get; }

    private DelayBracketId(Guid value) => Value = value;

    public static DelayBracketId New() => new(Guid.NewGuid());

    public static DelayBracketId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("DelayBracketId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
