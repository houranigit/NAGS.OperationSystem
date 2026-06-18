using BuildingBlocks.Application.Abstractions.Commands;

namespace Store.Application.Features.GeneralSupport.Commands.CreateGeneralSupport;

public sealed record CreateGeneralSupportCommand(
    string Name,
    Guid UnitId,
    bool IsDuration,
    string? Note,
    bool IsActive) : ICommand<Guid>;
