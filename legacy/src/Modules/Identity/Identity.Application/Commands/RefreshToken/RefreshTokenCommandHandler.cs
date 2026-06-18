using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Identity.Application.Commands.Login;
using Identity.Domain.Aggregates.Role;
using Identity.Domain.Aggregates.User;
using Identity.Domain.Aggregates.UserSession;

namespace Identity.Application.Commands.RefreshToken;

public sealed class RefreshTokenCommandHandler(
    IUserSessionRepository sessionRepository,
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    ITokenService tokenService)
    : ICommandHandler<RefreshTokenCommand, LoginResult>
{
    public async Task<Result<LoginResult>> Handle(
        RefreshTokenCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Find session by refresh token
        var session = await sessionRepository.GetByRefreshTokenAsync(command.RefreshToken, cancellationToken);
        if (session is null)
            return Error.NotFound("Session not found.");

        // 2. Check session is active
        if (!session.IsActive)
            return Error.Unauthorized("Session is no longer active.");

        // 3. Load user with roles
        var userWithRoles = await userRepository.GetByIdWithRolesAsync(session.UserId, cancellationToken);
        if (userWithRoles is null)
            return Error.NotFound("User not found.");

        // 4. Load roles with permissions
        var roleIds = userWithRoles.Roles.Select(r => r.RoleId).ToList();
        IReadOnlyList<string> permissions = [];
        if (roleIds.Count > 0)
        {
            var roles = await roleRepository.GetByIdsAsync(roleIds, cancellationToken);
            permissions = roles.SelectMany(r => r.GetPermissionCodes()).Distinct().ToList().AsReadOnly();
        }

        // 5. Generate new tokens
        var newAccessToken = tokenService.GenerateAccessToken(
            userWithRoles.Id.Value,
            userWithRoles.Email.Value,
            userWithRoles.Username.Value,
            userWithRoles.UserType.Name,
            permissions);

        var newRefreshToken = tokenService.GenerateRefreshToken();
        var now = DateTime.UtcNow;
        var newAccessTokenExpiresAt = now.Add(tokenService.AccessTokenExpiry);
        var newRefreshTokenExpiresAt = now.Add(tokenService.RefreshTokenExpiry);

        // 6. Refresh session
        var refreshResult = session.Refresh(
            newAccessToken,
            newRefreshToken,
            newAccessTokenExpiresAt,
            newRefreshTokenExpiresAt);

        if (!refreshResult.IsSuccess)
            return refreshResult.Error;

        sessionRepository.Update(session);

        return new LoginResult(
            newAccessToken,
            newRefreshToken,
            newAccessTokenExpiresAt,
            newRefreshTokenExpiresAt,
            userWithRoles.Id.Value,
            userWithRoles.Username.Value,
            userWithRoles.Email.Value,
            userWithRoles.UserType.Name,
            permissions);
    }
}
