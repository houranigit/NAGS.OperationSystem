using System.Text.RegularExpressions;
using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>Formatted work order number: {STATION_CODE}-{sequence}, e.g. RUH-0001.</summary>
public sealed class WorkOrderNumber : ValueObject
{
    private static readonly Regex ValidPattern = new(
        "^[A-Z]{3}-[0-9]{4,6}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public string Value { get; }

    private WorkOrderNumber(string value) => Value = value;

    /// <summary>Validates a full work order number string (already formatted).</summary>
    public static Result<WorkOrderNumber> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Error.Validation("Work order number is required.");

        var v = raw.Trim().ToUpperInvariant();
        if (!ValidPattern.IsMatch(v))
            return Error.Validation("Work order number must match {STATION_CODE}-{nnnn} (4 to 6 digits).");

        return new WorkOrderNumber(v);
    }

    /// <summary>Builds a number from station IATA/airport code (3 letters) and a 1-based sequence.</summary>
    public static Result<WorkOrderNumber> FromStationSequence(string stationAirportCode, long sequence)
    {
        if (string.IsNullOrWhiteSpace(stationAirportCode))
            return Error.Validation("Station code is required.");

        var code = stationAirportCode.Trim().ToUpperInvariant();
        if (code.Length != 3 || !code.All(char.IsAsciiLetter))
            return Error.Validation("Station code must be exactly 3 letters.");

        if (sequence < 1)
            return Error.Validation("Sequence must be positive.");

        var formatted = sequence < 10_000
            ? $"{code}-{sequence:D4}"
            : sequence < 100_000
                ? $"{code}-{sequence:D5}"
                : $"{code}-{sequence:D6}";

        return new WorkOrderNumber(formatted);
    }

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
