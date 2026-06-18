using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Identity.Contracts.IntegrationEvents;
using Identity.Domain.Aggregates.Role;
using Identity.Domain.Aggregates.User;
using Identity.Domain.Aggregates.UserSession;
using Identity.Domain.Enumerations;
using Identity.Domain.Policies;
using Identity.Domain.Services;

namespace Identity.Application.Commands.Login;

public sealed class LoginCommandHandler(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IUserSessionRepository sessionRepository,
    PasswordService passwordService,
    ITokenService tokenService,
    IOutboxWriter outboxWriter)
    : ICommandHandler<LoginCommand, LoginResult>
{
    public async Task<Result<LoginResult>> Handle(
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByEmailOrUsernameAsync(command.EmailOrUsername, cancellationToken);
        if (user is null)
        {
            outboxWriter.Write(
                nameof(UserLoginFailedIntegrationEvent),
                JsonSerializer.Serialize(new UserLoginFailedIntegrationEvent(
                    command.EmailOrUsername, command.IpAddress, "User not found")));
            return Error.Unauthorized("Invalid credentials.");
        }

        if (user.Status == UserStatus.Deactivated)
            return Error.Unauthorized("Account is deactivated.");

        if (user.Status == UserStatus.PendingActivation)
            return Error.Unauthorized("Account is pending activation.");

        if (user.Status == UserStatus.Locked)
        {
            if (user.LockedUntil.HasValue && DateTime.UtcNow > user.LockedUntil.Value)
                user.Unlock();
            else
                return Error.Unauthorized("Account is locked. Please try again later.");
        }

        if (user.Status == UserStatus.PasswordExpired)
            return Error.Unauthorized("Password has expired. Please change your password.");

        if (user.PasswordHash is null)
            return Error.Unauthorized("Invalid credentials.");

        var passwordValid = passwordService.VerifyPassword(command.Password, user.PasswordHash);
        if (!passwordValid)
        {
            user.RecordFailedLogin(LockoutPolicy.Default);
            userRepository.Update(user);

            if (user.Status == UserStatus.Locked)
            {
                outboxWriter.Write(
                    nameof(UserLockedIntegrationEvent),
                    JsonSerializer.Serialize(new UserLockedIntegrationEvent(
                        user.Id.Value, user.Username.Value, user.LockedUntil ?? DateTime.UtcNow)));
            }
            else
            {
                outboxWriter.Write(
                    nameof(UserLoginFailedIntegrationEvent),
                    JsonSerializer.Serialize(new UserLoginFailedIntegrationEvent(
                        command.EmailOrUsername, command.IpAddress, "Invalid password")));
            }

            return Error.Unauthorized("Invalid credentials.");
        }

        var userWithRoles = await userRepository.GetByIdWithRolesAsync(user.Id, cancellationToken);
        var roleIds = userWithRoles?.Roles.Select(r => r.RoleId).ToList() ?? [];

        IReadOnlyList<string> permissions = [];
        if (roleIds.Count > 0)
        {
            var roles = await roleRepository.GetByIdsAsync(roleIds, cancellationToken);
            permissions = roles.SelectMany(r => r.GetPermissionCodes()).Distinct().ToList().AsReadOnly();
        }

        var accessToken = tokenService.GenerateAccessToken(
            user.Id.Value, user.Email.Value, user.Username.Value,
            user.UserType.Name, permissions);

        var refreshToken = tokenService.GenerateRefreshToken();
        var now = DateTime.UtcNow;
        var accessTokenExpiresAt = now.Add(tokenService.AccessTokenExpiry);
        var refreshTokenExpiresAt = now.Add(tokenService.RefreshTokenExpiry);

        var sessionResult = UserSession.Create(
            user.Id, accessToken, refreshToken,
            accessTokenExpiresAt, refreshTokenExpiresAt,
            command.DeviceInfo, command.IpAddress, command.UserAgent);

        if (!sessionResult.IsSuccess) return sessionResult.Error;

        sessionRepository.Add(sessionResult.Value);

        user.RecordSuccessfulLogin();
        userRepository.Update(user);

        outboxWriter.Write(
            nameof(UserLoggedInIntegrationEvent),
            JsonSerializer.Serialize(new UserLoggedInIntegrationEvent(
                user.Id.Value, user.Username.Value, command.IpAddress, command.DeviceInfo)));

        return new LoginResult(
            accessToken, refreshToken,
            accessTokenExpiresAt, refreshTokenExpiresAt,
            user.Id.Value, user.Username.Value, user.Email.Value,
            user.UserType.Name, permissions);
    }
}
