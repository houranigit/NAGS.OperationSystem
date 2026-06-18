using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.ValueObjects;

/// <summary>Point-in-time copy of a Core aircraft type.</summary>
public sealed class AircraftTypeSnapshot : ValueObject
{
    public Guid AircraftTypeId { get; private set; }
    public string Model { get; private set; } = null!;

    private AircraftTypeSnapshot() { }

    private AircraftTypeSnapshot(Guid aircraftTypeId, string model)
    {
        AircraftTypeId = aircraftTypeId;
        Model = model;
    }

    public static Result<AircraftTypeSnapshot> Create(Guid aircraftTypeId, string? model)
    {
        if (aircraftTypeId == Guid.Empty)
            return Error.Validation("AircraftTypeId is required.");

        if (string.IsNullOrWhiteSpace(model))
            return Error.Validation("Aircraft type model is required.");

        if (model.Length > 100)
            return Error.Validation("Aircraft type model must not exceed 100 characters.");

        return new AircraftTypeSnapshot(aircraftTypeId, model.Trim());
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return AircraftTypeId;
        yield return Model;
    }
}
