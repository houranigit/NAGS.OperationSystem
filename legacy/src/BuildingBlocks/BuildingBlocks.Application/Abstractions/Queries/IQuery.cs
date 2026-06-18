using BuildingBlocks.Domain.Results;
using MediatR;

namespace BuildingBlocks.Application.Abstractions.Queries;

public interface IQuery<TResponse> : IRequest<Result<TResponse>> { }
