using BuildingBlocks.Application.Abstractions.Commands;

namespace Identity.Application.Commands.CreateUser;

public sealed record CreateUserCommand(
    string Username,
    string Email,
    string Password
) : ICommand<CreateUserResult>;

public sealed record CreateUserResult(Guid UserId);
