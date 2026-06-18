using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract;

public sealed class CancellationBracketId : ValueObject
{
    public Guid Value { get; }

    private CancellationBracketId(Guid value) => Value = value;

    public static CancellationBracketId New() => new(Guid.NewGuid());

    public static CancellationBracketId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("CancellationBracketId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
