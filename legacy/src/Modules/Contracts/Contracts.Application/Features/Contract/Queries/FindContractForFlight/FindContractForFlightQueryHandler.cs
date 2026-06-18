using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Domain.Results;
using Contracts.Contracts.Contract;
using Contracts.Contracts.Readers;

namespace Contracts.Application.Features.Contract.Queries.FindContractForFlight;

public sealed class FindContractForFlightQueryHandler(IContractReadService reader)
    : IQueryHandler<FindContractForFlightQuery, FindContractForFlightDto>
{
    public async Task<Result<FindContractForFlightDto>> Handle(
        FindContractForFlightQuery request,
        CancellationToken cancellationToken)
    {
        var result = await reader.FindActiveContractForFlightAsync(
            request.CustomerId,
            request.StationId,
            request.OperationTypeId,
            request.Sta,
            cancellationToken);

        return result.Outcome switch
        {
            // Single-AOG-service contracts mark assignment optional on the flight wizard
            // (the AOG flight is visible to every employee at the station and any of them
            // can claim it). Same rule lives on FlightCreateHelpers — keep both in sync.
            FindContractOutcome.Found when result.Contract is { } c =>
                new FindContractForFlightDto(
                    FindContractForFlightOutcome.Found,
                    c.ContractId,
                    c.ContractNumber,
                    IsAogOnly: c.OperationTypeServices.Count == 1 && c.OperationTypeServices[0].IsAog),
            FindContractOutcome.Ambiguous =>
                new FindContractForFlightDto(FindContractForFlightOutcome.Ambiguous, null, null, false),
            _ =>
                new FindContractForFlightDto(FindContractForFlightOutcome.NotFound, null, null, false),
        };
    }
}
