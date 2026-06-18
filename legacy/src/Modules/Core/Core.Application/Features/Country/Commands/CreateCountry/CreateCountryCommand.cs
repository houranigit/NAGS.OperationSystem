using BuildingBlocks.Application.Abstractions.Commands;

namespace Core.Application.Features.Country.Commands.CreateCountry;

public sealed record CreateCountryCommand(
    string Code,
    string Name,
    bool IsActive) : ICommand<Guid>;
