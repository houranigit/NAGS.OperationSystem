namespace Operations.Application.Abstractions;

/// <summary>
/// Allocates the next per-station work-order sequence value. Implemented in Infrastructure using a
/// serializable transaction (the explicit exception to optimistic concurrency for business numbers).
/// </summary>
public interface IWorkOrderNumberAllocator
{
    public Task<int> NextAsync(Guid stationId, string stationIata, CancellationToken cancellationToken);
}
