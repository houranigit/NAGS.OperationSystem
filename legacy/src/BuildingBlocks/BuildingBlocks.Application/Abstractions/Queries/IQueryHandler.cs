using BuildingBlocks.Domain.Results;
using MediatR;

namespace BuildingBlocks.Application.Abstractions.Queries;

public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse> { }
