using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Domain.Enumerations;

namespace Core.Application.Features.AircraftType.Commands.CreateAircraftType;

public sealed class CreateAircraftTypeCommandHandler(Core.Domain.Aggregates.AircraftType.IAircraftTypeRepository aircraftTypes)
    : ICommandHandler<CreateAircraftTypeCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateAircraftTypeCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
            return Error.Validation("Model is required.");

        if (await aircraftTypes.ExistsByManufacturerAndModelAsync(
                request.Manufacturer,
                request.Model,
                excludeId: null,
                cancellationToken))
            return Error.Conflict("An aircraft type with this manufacturer and model already exists.");

        var created = Core.Domain.Aggregates.AircraftType.AircraftType.Create(
            request.Manufacturer,
            request.Model,
            request.Notes);
        if (created.IsFailure)
            return created.Error;

        var aircraftType = created.Value;

        if (!request.IsActive)
        {
            var deactivated = aircraftType.Deactivate();
            if (deactivated.IsFailure)
                return deactivated.Error;
        }

        aircraftTypes.Add(aircraftType);

        return aircraftType.Id.Value;
    }
}
