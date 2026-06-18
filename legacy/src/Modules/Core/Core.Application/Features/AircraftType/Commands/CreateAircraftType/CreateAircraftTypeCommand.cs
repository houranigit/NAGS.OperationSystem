using BuildingBlocks.Application.Abstractions.Commands;
using Core.Domain.Enumerations;

namespace Core.Application.Features.AircraftType.Commands.CreateAircraftType;

public sealed record CreateAircraftTypeCommand(
    Manufacturer Manufacturer,
    string Model,
    string? Notes,
    bool IsActive) : ICommand<Guid>;
