using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.ValueObjects;

/// <summary>Point-in-time copy of a Core station.</summary>
public sealed class StationSnapshot : ValueObject
{
    public Guid StationId { get; private set; }
    public string IataCode { get; private set; } = null!;
    public string Name { get; private set; } = null!;

    private StationSnapshot() { }

    private StationSnapshot(Guid stationId, string iataCode, string name)
    {
        StationId = stationId;
        IataCode = iataCode;
        Name = name;
    }

    public static Result<StationSnapshot> Create(Guid stationId, string? iataCode, string? name)
    {
        if (stationId == Guid.Empty)
            return Error.Validation("StationId is required.");

        if (string.IsNullOrWhiteSpace(iataCode))
            return Error.Validation("Station IATA code is required.");

        var normalized = iataCode.Trim().ToUpperInvariant();
        if (normalized.Length != 3)
            return Error.Validation("Station IATA code must be exactly 3 characters.");

        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Station name is required.");

        if (name.Length > 200)
            return Error.Validation("Station name must not exceed 200 characters.");

        return new StationSnapshot(stationId, normalized, name.Trim());
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return StationId;
        yield return IataCode;
        yield return Name;
    }
}
