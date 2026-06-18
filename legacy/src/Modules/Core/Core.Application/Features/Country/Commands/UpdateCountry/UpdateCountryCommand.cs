using BuildingBlocks.Application.Abstractions.Commands;

namespace Core.Application.Features.Country.Commands.UpdateCountry;

public sealed record UpdateCountryCommand(
    Guid Id,
    string Code,
    string Name,
    bool IsActive) : ICommand;
