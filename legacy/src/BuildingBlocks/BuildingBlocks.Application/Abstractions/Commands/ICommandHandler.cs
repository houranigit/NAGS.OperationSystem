using BuildingBlocks.Domain.Results;
using MediatR;

namespace BuildingBlocks.Application.Abstractions.Commands;

public interface ICommandHandler<TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand { }

public interface ICommandHandler<TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse> { }
