using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using MediatR;

namespace BuildingBlocks.Application.Behaviors;

public sealed class TransactionBehavior<TRequest, TResponse>(IEnumerable<IUnitOfWork> unitOfWorks)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ITransactional)
            return await next();

        var response = await next();

        // Only persist if the command succeeded
        var isSuccess = response switch
        {
            Result r => r.IsSuccess,
            _ => true
        };

        if (isSuccess)
            foreach (var uow in unitOfWorks)
                await uow.SaveChangesAsync(cancellationToken);

        return response;
    }
}
