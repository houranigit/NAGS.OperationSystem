namespace Core.Contracts.Features.Station;

public sealed record StationSelectOption(
    Guid Id,
    string Name,
    string IataCode);
