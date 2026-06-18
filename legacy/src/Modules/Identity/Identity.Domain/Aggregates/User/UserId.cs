using BuildingBlocks.Domain.ValueObjects;

namespace Identity.Domain.Aggregates.User;

public sealed class UserId : ValueObject
{
    public Guid Value { get; }

    private UserId(Guid value) => Value = value;

    public static UserId New() => new(Guid.NewGuid());

    public static UserId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("UserId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
