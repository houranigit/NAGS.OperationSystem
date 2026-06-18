using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.ValueObjects;

public sealed class CurrencyCode : ValueObject
{
    public string Value { get; }

    private CurrencyCode(string value) => Value = value;

    public static Result<CurrencyCode> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Error.Validation("Currency code is required.");

        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length != 3)
            return Error.Validation("Currency code must be exactly 3 characters (ISO 4217).");

        if (!normalized.All(char.IsAsciiLetter))
            return Error.Validation("Currency code must contain only letters.");

        return new CurrencyCode(normalized);
    }

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
