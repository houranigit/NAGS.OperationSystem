using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Domain.Results;
using MediatR;

namespace BuildingBlocks.Application.Behaviors;

public sealed class AuthorizationBehavior<TRequest, TResponse>(
    ICurrentUserService currentUserService)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IRequirePermission requirePermission)
            return await next();

        if (!currentUserService.IsAuthenticated)
            return CreateUnauthorized("User is not authenticated.");

        if (!currentUserService.HasPermission(requirePermission.RequiredPermission))
            return CreateUnauthorized($"Permission '{requirePermission.RequiredPermission}' is required.");

        return await next();
    }

    private static TResponse CreateUnauthorized(string message)
    {
        var error = Error.Unauthorized(message);

        // TResponse is either Result or Result<T>
        var responseType = typeof(TResponse);

        if (responseType == typeof(Result))
            return (TResponse)(object)Result.Failure(error);

        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var failureMethod = responseType.GetMethod("Failure",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                [typeof(Error)]);

            if (failureMethod is not null)
                return (TResponse)failureMethod.Invoke(null, [error])!;
        }

        throw new InvalidOperationException(
            $"AuthorizationBehavior: Cannot create failure response for type {responseType.Name}.");
    }
}
