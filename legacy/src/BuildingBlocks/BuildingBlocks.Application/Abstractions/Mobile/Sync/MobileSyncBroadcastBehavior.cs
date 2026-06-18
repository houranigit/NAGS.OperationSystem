using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Application.Abstractions.Mobile.Sync;

/// <summary>
/// MediatR pipeline behavior that drains the per-request <see cref="IMobileSyncBroadcaster"/>
/// buffer once the inner pipeline (including <c>TransactionBehavior</c>) has finished
/// successfully. Must be registered <em>outside</em> the transaction behavior so
/// <see cref="IMobileSyncBroadcaster.FlushAsync"/> runs after the unit-of-work commits —
/// otherwise we could push a change that the server ultimately rolled back. In
/// practice this is achieved by registering this behavior <em>before</em>
/// <c>AddBuildingBlocksApplication</c> in <c>Program.cs</c>, since MediatR resolves
/// pipeline behaviors in DI registration order (first = outermost).
/// </summary>
/// <remarks>
/// Only flushes for <see cref="ITransactional"/> commands — queries and read-only
/// requests don't mutate data so their broadcaster scope is always empty anyway,
/// and skipping the call avoids an unnecessary dependency resolution.
/// </remarks>
public sealed class MobileSyncBroadcastBehavior<TRequest, TResponse>(
    IMobileSyncBroadcaster broadcaster,
    ILogger<MobileSyncBroadcastBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next();

        if (request is not ITransactional)
            return response;

        var isSuccess = response switch
        {
            Result r => r.IsSuccess,
            _ => true
        };
        if (!isSuccess)
            return response;

        try
        {
            await broadcaster.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Live pushes are best-effort — a flush failure must never bubble up and
            // turn a successful command into a failed one. Mobile clients reconcile
            // via the catch-up endpoint on reconnect / next periodic poll.
            logger.LogWarning(ex,
                "Mobile sync broadcaster flush failed for request {RequestType}",
                typeof(TRequest).Name);
        }

        return response;
    }
}
