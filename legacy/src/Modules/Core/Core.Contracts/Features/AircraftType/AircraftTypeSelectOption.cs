namespace Core.Contracts.Features.AircraftType;

/// <summary>Active aircraft types ordered for lookups (matches GetAircraftTypeSelectOptions).</summary>
public sealed record AircraftTypeSelectOption(
    Guid Id,
    string Model);
