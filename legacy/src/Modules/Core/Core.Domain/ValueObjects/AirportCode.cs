using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.ValueObjects;

public sealed class AirportCode : ValueObject
{
    public string Value { get; }

    private AirportCode(string value) => Value = value;

    public static Result<AirportCode> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Error.Validation("Airport code is required.");

        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length != 3)
            return Error.Validation("Airport code must be exactly 3 characters (IATA).");

        if (!normalized.All(char.IsAsciiLetter))
            return Error.Validation("Airport code must contain only letters.");

        return new AirportCode(normalized);
    }

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
