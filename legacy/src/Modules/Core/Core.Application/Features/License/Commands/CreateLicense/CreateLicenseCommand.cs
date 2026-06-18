using BuildingBlocks.Application.Abstractions.Commands;

namespace Core.Application.Features.License.Commands.CreateLicense;

public sealed record CreateLicenseCommand(
    string Code,
    string Name,
    string? Description,
    bool IsActive) : ICommand<Guid>;
