using MediatR;

namespace Host.Web.Services;

/// <summary>
/// Runs each MediatR request in a new DI scope so concurrent handlers never share the same DbContext
/// (required for Blazor Server when FullCalendar or other controls fire overlapping loads).
/// </summary>
public interface IScopedMediator
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}
