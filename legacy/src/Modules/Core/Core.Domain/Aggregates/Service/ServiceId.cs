using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.Aggregates.Service;

public sealed class ServiceId : ValueObject
{
    public Guid Value { get; }

    private ServiceId(Guid value) => Value = value;

    public static ServiceId New() => new(Guid.NewGuid());

    public static ServiceId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("ServiceId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
