using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.Station;

namespace Core.Application.Features.Station.Commands.UpdateStation;

public sealed class UpdateStationCommandHandler(IStationRepository stations)
    : ICommandHandler<UpdateStationCommand>
{
    public async Task<Result> Handle(UpdateStationCommand request, CancellationToken cancellationToken)
    {
        var id = StationId.From(request.Id);
        var entity = await stations.GetByIdAsync(id, cancellationToken);
        if (entity is null) return Error.NotFound("Station was not found.");

        var detailsResult = entity.UpdateDetails(request.Name, request.City ?? string.Empty);
        if (detailsResult.IsFailure) return detailsResult;

        if (request.IsActive != entity.IsActive)
        {
            var toggle = request.IsActive ? entity.Activate() : entity.Deactivate();
            if (toggle.IsFailure) return toggle;
        }

        stations.Update(entity);
        return Result.Success();
    }
}
