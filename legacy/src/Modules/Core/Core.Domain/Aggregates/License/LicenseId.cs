using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.Aggregates.License;

public sealed class LicenseId : ValueObject
{
    public Guid Value { get; }

    private LicenseId(Guid value) => Value = value;

    public static LicenseId New() => new(Guid.NewGuid());

    public static LicenseId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("LicenseId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
