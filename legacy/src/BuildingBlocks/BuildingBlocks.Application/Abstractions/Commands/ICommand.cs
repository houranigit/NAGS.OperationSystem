using BuildingBlocks.Domain.Results;
using MediatR;

namespace BuildingBlocks.Application.Abstractions.Commands;

public interface ICommand : IRequest<Result>, ITransactional { }

public interface ICommand<TResponse> : IRequest<Result<TResponse>>, ITransactional { }

// Marker used by TransactionBehavior to identify commands that require a transaction
public interface ITransactional { }
