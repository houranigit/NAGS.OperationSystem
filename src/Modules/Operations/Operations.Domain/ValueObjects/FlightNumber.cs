using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>A normalized flight number (trimmed, upper-cased, max 12 chars).</summary>
public sealed class FlightNumber : ValueObject
{
    private FlightNumber() { }

    private FlightNumber(string value) => Value = value;

    public string Value { get; private set; } = null!;

    public static Result<FlightNumber> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Error.Validation("Flight number is required.", "Operations.FlightNumber.Required");

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > 12)
            return Error.Validation("Flight number must be at most 12 characters.", "Operations.FlightNumber.TooLong");

        return new FlightNumber(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
