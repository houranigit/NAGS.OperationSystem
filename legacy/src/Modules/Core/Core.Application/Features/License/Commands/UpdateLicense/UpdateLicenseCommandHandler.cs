using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.License;

namespace Core.Application.Features.License.Commands.UpdateLicense;

/// <summary>
/// Updates license details and active flag. License code is not changed after creation (<see cref="License"/> exposes no mutable code setter).
/// </summary>
public sealed class UpdateLicenseCommandHandler(ILicenseRepository licenses)
    : ICommandHandler<UpdateLicenseCommand>
{
    public async Task<Result> Handle(UpdateLicenseCommand request, CancellationToken cancellationToken)
    {
        var id = LicenseId.From(request.Id);
        var entity = await licenses.GetByIdAsync(id, cancellationToken);
        if (entity is null) return Error.NotFound("License was not found.");

        var detailsResult = entity.UpdateDetails(request.Name, request.Description);
        if (detailsResult.IsFailure) return detailsResult;

        if (request.IsActive != entity.IsActive)
        {
            var toggle = request.IsActive ? entity.Activate() : entity.Deactivate();
            if (toggle.IsFailure) return toggle;
        }

        licenses.Update(entity);
        return Result.Success();
    }
}
