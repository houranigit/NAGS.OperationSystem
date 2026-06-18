using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.ValueObjects;

public sealed class CountryCode : ValueObject
{
    public string Value { get; }

    private CountryCode(string value) => Value = value;

    public static Result<CountryCode> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Error.Validation("Country code is required.");

        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length != 2)
            return Error.Validation("Country code must be exactly 2 characters (ISO 3166-1 alpha-2).");

        if (!normalized.All(char.IsAsciiLetter))
            return Error.Validation("Country code must contain only letters.");

        return new CountryCode(normalized);
    }

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
