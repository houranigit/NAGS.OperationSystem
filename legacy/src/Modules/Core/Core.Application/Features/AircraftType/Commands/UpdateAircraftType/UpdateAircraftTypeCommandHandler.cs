using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.AircraftType;

namespace Core.Application.Features.AircraftType.Commands.UpdateAircraftType;

public sealed class UpdateAircraftTypeCommandHandler(IAircraftTypeRepository aircraftTypes)
    : ICommandHandler<UpdateAircraftTypeCommand>
{
    public async Task<Result> Handle(UpdateAircraftTypeCommand request, CancellationToken cancellationToken)
    {
        if (request.Id == Guid.Empty)
            return Error.Validation("Id is required.");

        if (string.IsNullOrWhiteSpace(request.Model))
            return Error.Validation("Model is required.");

        var id = AircraftTypeId.From(request.Id);
        var entity = await aircraftTypes.GetByIdAsync(id, cancellationToken);
        if (entity is null)
            return Error.NotFound("Aircraft type was not found.");

        if (await aircraftTypes.ExistsByManufacturerAndModelAsync(
                request.Manufacturer,
                request.Model,
                id,
                cancellationToken))
            return Error.Conflict("Another aircraft type already uses this manufacturer and model.");

        var updated = entity.UpdateDetails(request.Manufacturer, request.Model, request.Notes);
        if (updated.IsFailure)
            return updated.Error;

        if (request.IsActive != entity.IsActive)
        {
            var toggle = request.IsActive ? entity.Activate() : entity.Deactivate();
            if (toggle.IsFailure)
                return toggle.Error;
        }

        aircraftTypes.Update(entity);
        return Result.Success();
    }
}
