using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Operations.Domain.ValueObjects;

/// <summary>
/// Human-facing per-station work-order number in the form <c>{IATA}-{nnnn}</c> (sequence
/// zero-padded to at least 4 digits). Allocated on approval from the station sequence.
/// </summary>
public sealed class WorkOrderNumber : ValueObject
{
    private WorkOrderNumber() { }

    private WorkOrderNumber(string value) => Value = value;

    public string Value { get; private set; } = null!;

    public static WorkOrderNumber FromStationSequence(string stationIata, int sequence)
    {
        var iata = stationIata.Trim().ToUpperInvariant();
        return new WorkOrderNumber($"{iata}-{sequence:D4}");
    }

    public static Result<WorkOrderNumber> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Error.Validation("Work order number is required.", "Operations.WorkOrderNumber.Required");

        return new WorkOrderNumber(value.Trim().ToUpperInvariant());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
