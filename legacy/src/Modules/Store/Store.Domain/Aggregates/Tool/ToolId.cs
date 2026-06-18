using BuildingBlocks.Domain.ValueObjects;

namespace Store.Domain.Aggregates.Tool;

public sealed class ToolId : ValueObject
{
    public Guid Value { get; }

    private ToolId(Guid value) => Value = value;

    public static ToolId New() => new(Guid.NewGuid());

    public static ToolId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("ToolId cannot be empty.")
            : new(value);

    public override string ToString() => Value.ToString();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
