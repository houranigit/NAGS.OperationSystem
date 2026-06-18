using BuildingBlocks.Domain.ValueObjects;

namespace Identity.Domain.Aggregates.UserSession;

public sealed class UserSessionId : ValueObject
{
    public Guid Value { get; }

    private UserSessionId(Guid value) => Value = value;

    public static UserSessionId New() => new(Guid.NewGuid());

    public static UserSessionId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("UserSessionId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
