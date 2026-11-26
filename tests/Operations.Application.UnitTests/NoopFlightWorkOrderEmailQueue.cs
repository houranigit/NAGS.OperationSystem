using BuildingBlocks.Domain.Results;
using Operations.Application.Features.Flights;
using Operations.Domain.Flights;

namespace Operations.Application.UnitTests;

internal sealed class NoopFlightWorkOrderEmailQueue : IFlightWorkOrderEmailQueue
{
    public static readonly NoopFlightWorkOrderEmailQueue Instance = new();

    public Task<Result> EnqueueAsync(Flight flight, Guid actorUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success());
}
