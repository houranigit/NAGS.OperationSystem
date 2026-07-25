using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace OperationsSystem.Api;

/// <summary>
/// Converts optimistic-concurrency failures that escape an application handler into the same
/// stable HTTP contract used by handlers that detect a stale row version explicitly.
/// </summary>
public sealed class ConcurrencyExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateConcurrencyException)
            return false;

        await ApiResults.Problem(ConcurrencyErrors.Stale).ExecuteAsync(httpContext);
        return true;
    }
}
