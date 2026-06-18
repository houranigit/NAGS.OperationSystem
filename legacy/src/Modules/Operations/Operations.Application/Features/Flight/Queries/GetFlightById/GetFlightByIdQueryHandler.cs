using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Domain.Results;
using Operations.Application.Mapping;
using Operations.Domain.Aggregates.Flight;
using Operations.Contracts.Flight;

namespace Operations.Application.Features.Flight.Queries.GetFlightById;

public sealed class GetFlightByIdQueryHandler(IFlightRepository flights) : IQueryHandler<GetFlightByIdQuery, FlightDto?>
{
    public async Task<Result<FlightDto?>> Handle(GetFlightByIdQuery request, CancellationToken cancellationToken)
    {
        var id = FlightId.From(request.Id);
        var flight = await flights.GetByIdAsync(id, cancellationToken);
        if (flight is null)
            return Result<FlightDto?>.Success(null);

        return Result<FlightDto?>.Success(FlightDtoMapper.FromAggregate(flight));
    }
}
