using System.Text.RegularExpressions;
using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>A customer-prefixed flight number with 1–4 digits, an optional letter or route, max 12 chars.</summary>
public sealed class FlightNumber : ValueObject
{
    private FlightNumber() { }

    private FlightNumber(string value) => Value = value;

    public string Value { get; private set; } = null!;

    public static Result<FlightNumber> Create(string? value, string? customerIataCode = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Error.Validation("Flight number is required.", "Operations.FlightNumber.Required");

        var prefix = customerIataCode?.Trim().ToUpperInvariant() ?? string.Empty;
        const string pattern = @"\A[0-9]{1,4}(?:[A-Za-z]|/[0-9]{1,2})?\z";
        var hasPrefix = prefix.Length > 0 && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        var number = hasPrefix ? value[prefix.Length..] : value;
        var validNumber = Regex.IsMatch(number, pattern, RegexOptions.CultureInvariant);
        if (!validNumber && hasPrefix && Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant))
        {
            number = value;
            validNumber = true;
        }
        if (!validNumber)
            return Error.Validation(
                "Flight number must have 1–4 digits, optionally followed by either one letter or '/' and one or two route digits. The customer's IATA code may be included. Spaces and other characters are not allowed.",
                "Operations.FlightNumber.InvalidFormat");

        var normalized = $"{prefix}{number.ToUpperInvariant()}";
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
