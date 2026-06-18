using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Host.Web.Services;

public sealed class ScopedMediator(IServiceScopeFactory scopeFactory) : IScopedMediator
{
    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return await mediator.Send(request, cancellationToken);
    }
}
