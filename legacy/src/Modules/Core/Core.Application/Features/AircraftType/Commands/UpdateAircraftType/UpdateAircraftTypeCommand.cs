using BuildingBlocks.Application.Abstractions.Commands;
using Core.Domain.Enumerations;

namespace Core.Application.Features.AircraftType.Commands.UpdateAircraftType;

public sealed record UpdateAircraftTypeCommand(
    Guid Id,
    Manufacturer Manufacturer,
    string Model,
    string? Notes,
    bool IsActive) : ICommand;
