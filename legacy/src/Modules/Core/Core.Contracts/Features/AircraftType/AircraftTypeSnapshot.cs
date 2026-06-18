namespace Core.Contracts.Features.AircraftType;

/// <summary>Lean read-model for other modules (<c>string Manufacturer</c> matches cross-module payloads).</summary>
public sealed record AircraftTypeSnapshot(
    Guid AircraftTypeId,
    string Model);
