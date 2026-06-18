namespace Core.Contracts.Features.Station;

public sealed record StationLightDto(
    Guid Id,
    string IataCode,
    string? IcaoCode,
    string Name,
    string? City,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
