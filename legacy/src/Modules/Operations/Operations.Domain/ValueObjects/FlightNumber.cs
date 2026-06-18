using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>Normalized IATA-style flight number (trim, upper case, max length).</summary>
public sealed class FlightNumber : ValueObject
{
    public const int MaxLength = 12;

    public string Value { get; }

    private FlightNumber(string value) => Value = value;

    public static Result<FlightNumber> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Error.Validation("Flight number is required.");

        var v = raw.Trim().ToUpperInvariant();
        if (v.Length > MaxLength)
            return Error.Validation($"Flight number must not exceed {MaxLength} characters.");

        return new FlightNumber(v);
    }

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
