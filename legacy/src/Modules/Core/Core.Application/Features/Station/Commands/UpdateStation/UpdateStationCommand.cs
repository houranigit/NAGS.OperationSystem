using BuildingBlocks.Application.Abstractions.Commands;

namespace Core.Application.Features.Station.Commands.UpdateStation;

public sealed record UpdateStationCommand(
    Guid Id,
    string IataCode,
    string? IcaoCode,
    string Name,
    string? City,
    Guid CountryId,
    bool IsActive,
    IReadOnlyList<Guid> AssignedEmployeeIds,
    IReadOnlyList<Guid> LicensesCoveredIds) : ICommand;
