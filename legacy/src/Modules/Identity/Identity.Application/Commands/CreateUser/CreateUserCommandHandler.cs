using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Identity.Contracts.IntegrationEvents;
using Identity.Domain.Aggregates.User;
using Identity.Domain.Services;
using Identity.Domain.ValueObjects;

namespace Identity.Application.Commands.CreateUser;

public sealed class CreateUserCommandHandler(
    IUserRepository repository,
    PasswordService passwordService,
    IOutboxWriter outboxWriter)
    : ICommandHandler<CreateUserCommand, CreateUserResult>
{
    public async Task<Result<CreateUserResult>> Handle(
        CreateUserCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await repository.GetByEmailAsync(command.Email, cancellationToken);
        if (existing is not null)
            return Error.Conflict($"A user with email '{command.Email}' already exists.");

        var usernameResult = Username.Create(command.Username);
        if (!usernameResult.IsSuccess) return usernameResult.Error;

        var emailResult = Email.Create(command.Email);
        if (!emailResult.IsSuccess) return emailResult.Error;

        var passwordHashResult = passwordService.HashPassword(command.Password);
        if (!passwordHashResult.IsSuccess) return passwordHashResult.Error;

        var userResult = User.Create(usernameResult.Value, emailResult.Value, passwordHashResult.Value);
        if (!userResult.IsSuccess) return userResult.Error;

        var user = userResult.Value;
        repository.Add(user);

        outboxWriter.Write(
            nameof(UserCreatedIntegrationEvent),
            JsonSerializer.Serialize(new UserCreatedIntegrationEvent(
                user.Id.Value,
                user.Username.Value,
                user.Email.Value,
                user.UserType.Name)));

        return new CreateUserResult(user.Id.Value);
    }
}
