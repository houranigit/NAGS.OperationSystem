using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Identity.Contracts.IntegrationEvents;
using Identity.Domain.Aggregates.User;
using Identity.Domain.Authorization;
using Identity.Domain.Policies;
using Identity.Domain.Services;

namespace Identity.Application.Commands.ChangePassword;

public sealed class ChangePasswordCommandHandler(
    IUserRepository userRepository,
    IPasswordHistoryRepository passwordHistoryRepository,
    PasswordService passwordService,
    ICurrentUserService currentUserService,
    IOutboxWriter outboxWriter)
    : ICommandHandler<ChangePasswordCommand>
{
    public async Task<Result> Handle(
        ChangePasswordCommand command,
        CancellationToken cancellationToken)
    {
        var isOwner = currentUserService.UserId == command.UserId;
        var hasPermission = currentUserService.HasPermission(Permissions.Users.Update);

        if (!isOwner && !hasPermission)
            return Error.Unauthorized("You do not have permission to change this user's password.");

        var userId = UserId.From(command.UserId);
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return Error.NotFound("User not found.");

        if (user.PasswordHash is null)
            return Error.Conflict("User has no password set.");

        if (!passwordService.VerifyPassword(command.CurrentPassword, user.PasswordHash))
            return Error.Validation("Current password is incorrect.");

        var policy = PasswordPolicy.Default;
        var history = await passwordHistoryRepository.GetLastNAsync(userId, policy.HistoryCount, cancellationToken);

        var hashResult = passwordService.ValidateAndHashPassword(command.NewPassword, history);
        if (!hashResult.IsSuccess) return hashResult.Error;

        var changeResult = user.ChangePassword(hashResult.Value, policy);
        if (!changeResult.IsSuccess) return changeResult.Error;

        passwordHistoryRepository.Add(PasswordHistoryEntry.Create(userId, hashResult.Value));
        userRepository.Update(user);

        outboxWriter.Write(
            nameof(UserPasswordChangedIntegrationEvent),
            JsonSerializer.Serialize(new UserPasswordChangedIntegrationEvent(
                user.Id.Value, user.Username.Value)));

        return Result.Success();
    }
}
