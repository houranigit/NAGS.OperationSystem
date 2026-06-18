using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.Country;
using Core.Domain.Aggregates.Station;
using Core.Domain.ValueObjects;

namespace Core.Application.Features.Station.Commands.CreateStation;

public sealed class CreateStationCommandHandler(IStationRepository stations)
    : ICommandHandler<CreateStationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateStationCommand request, CancellationToken cancellationToken)
    {
        var iataCodeResult = AirportCode.Create(request.IataCode);
        if (iataCodeResult.IsFailure) return iataCodeResult.Error;

        if (await stations.ExistsByIataCodeAsync(iataCodeResult.Value.Value, cancellationToken))
            return Error.Conflict("A station with this IATA code already exists.");

        var created = Core.Domain.Aggregates.Station.Station.Create(
            iataCodeResult.Value,
            request.Name,
            request.City ?? string.Empty,
            CountryId.From(request.CountryId));
        if (created.IsFailure) return created.Error;

        var station = created.Value;

        if (!request.IsActive)
        {
            var d = station.Deactivate();
            if (d.IsFailure) return d.Error;
        }

        stations.Add(station);
        return station.Id.Value;
    }
}
