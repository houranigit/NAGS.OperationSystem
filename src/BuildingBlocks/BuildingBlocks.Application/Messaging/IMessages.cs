using BuildingBlocks.Domain.Results;
using MediatR;

namespace BuildingBlocks.Application.Messaging;

/// <summary>A command that mutates state and represents one business transaction.</summary>
public interface ICommand : IRequest<Result>;

/// <summary>A command that mutates state and returns a value.</summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>;

/// <summary>A read-only query that projects to a read model.</summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;

public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;
