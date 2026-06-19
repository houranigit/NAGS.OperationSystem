using BuildingBlocks.Domain.Results;
using FluentValidation;
using MediatR;

namespace BuildingBlocks.Application.Behaviors;

/// <summary>
/// Runs all FluentValidation validators for a request before the handler executes. On failure
/// it short-circuits with a failed <see cref="Result"/> carrying field-level errors, rather
/// than throwing — keeping the Result-first contract intact end to end.
/// </summary>
public sealed class ValidationPipelineBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.Distinct().ToArray());

        if (failures.Count == 0)
            return await next();

        var error = Error.Validation(failures);
        return CreateFailure(error);
    }

    private static TResponse CreateFailure(Error error)
    {
        if (typeof(TResponse) == typeof(Result))
            return (TResponse)(object)Result.Failure(error);

        var valueType = typeof(TResponse).GetGenericArguments()[0];
        var failureMethod = typeof(Result)
            .GetMethods()
            .First(m => m is { Name: nameof(Result.Failure), IsGenericMethod: true })
            .MakeGenericMethod(valueType);

        return (TResponse)failureMethod.Invoke(null, [error])!;
    }
}
