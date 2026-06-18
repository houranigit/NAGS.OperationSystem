namespace Core.Contracts.Features.Station;

public sealed record StationSnapshot(
    Guid StationId,
    string Name,
    string IataCode);
