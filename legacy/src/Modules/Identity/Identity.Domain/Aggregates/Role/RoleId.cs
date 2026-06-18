using BuildingBlocks.Domain.ValueObjects;

namespace Identity.Domain.Aggregates.Role;

public sealed class RoleId : ValueObject
{
    public Guid Value { get; }

    private RoleId(Guid value) => Value = value;

    public static RoleId New() => new(Guid.NewGuid());

    public static RoleId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("RoleId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
