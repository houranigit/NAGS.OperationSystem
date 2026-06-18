using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Identity.Domain.Aggregates.Role;
using Identity.Domain.Aggregates.User;
using Identity.Domain.Aggregates.UserSession;
using Identity.Domain.Policies;
using Identity.Domain.Services;

namespace Identity.Application.Commands.ActivateAccount;

public sealed class ActivateAccountCommandHandler(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IPasswordHistoryRepository passwordHistoryRepository,
    IUserSessionRepository sessionRepository,
    PasswordService passwordService,
    ITokenService tokenService)
    : ICommandHandler<ActivateAccountCommand, ActivateAccountResult>
{
    public async Task<Result<ActivateAccountResult>> Handle(
        ActivateAccountCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Find invited user by email (token is verified on the aggregate)
        var user = await userRepository.GetByEmailAsync(command.Email, cancellationToken);
        if (user is null)
            return Error.Validation("Invalid email or activation code.");

        // 2. Hash password (no history for new user activation)
        var hashResult = passwordService.ValidateAndHashPassword(command.Password, []);
        if (!hashResult.IsSuccess)
            return hashResult.Error;

        // 3. Activate from invitation
        var policy = PasswordPolicy.Default;
        var activateResult = user.ActivateFromInvitation(command.InvitationToken, hashResult.Value, policy);
        if (!activateResult.IsSuccess)
            return activateResult.Error;

        // 4. Save password history entry
        passwordHistoryRepository.Add(PasswordHistoryEntry.Create(user.Id, hashResult.Value));

        userRepository.Update(user);

        var userWithRoles = await userRepository.GetByIdWithRolesAsync(user.Id, cancellationToken);
        var roleIds = userWithRoles?.Roles.Select(r => r.RoleId).ToList() ?? [];

        IReadOnlyList<string> permissions = [];
        if (roleIds.Count > 0)
        {
            var roles = await roleRepository.GetByIdsAsync(roleIds, cancellationToken);
            permissions = roles.SelectMany(r => r.GetPermissionCodes()).Distinct().ToList().AsReadOnly();
        }

        var accessToken = tokenService.GenerateAccessToken(
            user.Id.Value,
            user.Email.Value,
            user.Username.Value,
            user.UserType.Name,
            permissions);

        var refreshToken = tokenService.GenerateRefreshToken();
        var now = DateTime.UtcNow;
        var accessTokenExpiresAt = now.Add(tokenService.AccessTokenExpiry);
        var refreshTokenExpiresAt = now.Add(tokenService.RefreshTokenExpiry);

        var sessionResult = UserSession.Create(
            user.Id,
            accessToken,
            refreshToken,
            accessTokenExpiresAt,
            refreshTokenExpiresAt,
            command.DeviceInfo,
            command.IpAddress,
            command.UserAgent);

        if (!sessionResult.IsSuccess)
            return sessionResult.Error;

        sessionRepository.Add(sessionResult.Value);

        return new ActivateAccountResult(
            accessToken,
            refreshToken,
            accessTokenExpiresAt,
            refreshTokenExpiresAt,
            user.Id.Value,
            user.Username.Value,
            user.Email.Value,
            user.UserType.Name,
            permissions);
    }
}
