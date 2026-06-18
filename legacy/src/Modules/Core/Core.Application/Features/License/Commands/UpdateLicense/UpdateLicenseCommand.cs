using BuildingBlocks.Application.Abstractions.Commands;

namespace Core.Application.Features.License.Commands.UpdateLicense;

public sealed record UpdateLicenseCommand(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive) : ICommand;
