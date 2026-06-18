using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Core.Domain.ValueObjects;

public sealed class IataAirlineCode : ValueObject
{
    public string Value { get; }

    private IataAirlineCode(string value) => Value = value;

    public static Result<IataAirlineCode> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Error.Validation("IATA airline code is required.");

        var code = raw.Trim().ToUpperInvariant();

        if (code.Length != 2)
            return Error.Validation("IATA airline code must be exactly 2 characters.");

        if (!code.All(c => char.IsLetterOrDigit(c)))
            return Error.Validation("IATA airline code must contain only letters or digits.");

        return new IataAirlineCode(code);
    }

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
