using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using MediatR;
using Microsoft.Extensions.Logging;
using Operations.Application.Abstractions;

namespace Operations.Application.Behaviors;

/// <summary>
/// Invalidates the operations dashboard after a successful Operations command has completed.
/// Command handlers persist their changes before returning, so the notification is post-commit.
/// Queries and commands from other modules pass through without publishing.
/// </summary>
public sealed class OperationsDashboardRealtimeBehavior<TRequest, TResponse>(
    IOperationsDashboardRealtimeNotifier notifier,
    ILogger<OperationsDashboardRealtimeBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private static readonly bool IsOperationsCommand =
        typeof(TRequest).Assembly == AssemblyReference.Assembly &&
        (typeof(ICommand).IsAssignableFrom(typeof(TRequest)) ||
         typeof(TRequest).GetInterfaces().Any(contract =>
             contract.IsGenericType &&
             contract.GetGenericTypeDefinition() == typeof(ICommand<>)));

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next();
        if (!IsOperationsCommand || response.IsFailure)
            return response;

        try
        {
            await notifier.NotifyChangedAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // The REST dashboard is authoritative. A transient live-delivery failure must not turn
            // an already committed business command into a failed request.
            logger.LogWarning(
                ex,
                "Operations dashboard realtime invalidation failed for request {RequestType}",
                typeof(TRequest).Name);
        }

        return response;
    }
}
