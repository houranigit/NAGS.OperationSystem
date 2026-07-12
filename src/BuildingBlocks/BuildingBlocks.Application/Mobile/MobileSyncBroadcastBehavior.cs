using BuildingBlocks.Domain.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Application.Mobile;

/// <summary>
/// MediatR pipeline behavior that drains the per-request <see cref="IMobileSyncBroadcaster"/>
/// buffer once the inner pipeline finished successfully. Command handlers call
/// <c>SaveChangesAsync</c> themselves, so flushing after a successful handler return is
/// post-commit. Register this behavior first (outermost) so it wraps validation and the
/// handler alike.
/// </summary>
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
            // Live pushes are best-effort — a flush failure must never turn a successful
            // command into a failed one. Clients reconcile via catch-up on reconnect.
            logger.LogWarning(ex,
                "Mobile sync broadcaster flush failed for request {RequestType}",
                typeof(TRequest).Name);
        }

        return response;
    }
}
