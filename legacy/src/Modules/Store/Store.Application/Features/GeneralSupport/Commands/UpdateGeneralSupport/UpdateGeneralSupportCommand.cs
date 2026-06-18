using BuildingBlocks.Application.Abstractions.Commands;

namespace Store.Application.Features.GeneralSupport.Commands.UpdateGeneralSupport;

public sealed record UpdateGeneralSupportCommand(
    Guid Id,
    string Name,
    Guid UnitId,
    bool IsDuration,
    string? Note,
    bool IsActive) : ICommand;
