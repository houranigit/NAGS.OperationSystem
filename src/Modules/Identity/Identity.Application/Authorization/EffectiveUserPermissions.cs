using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Domain.Results;
using Identity.Domain.Roles;
using Identity.Domain.Users;

namespace Identity.Application.Authorization;

internal static class EffectiveUserPermissions
{
    private static readonly Error InvalidRoleConfiguration = Error.Unauthorized(
        "The account's access configuration is invalid.",
        "Identity.Auth.InvalidRoleConfiguration");

    /// <summary>
    /// Resolves permissions only from a present, matching, currently valid role. Persisted
    /// corruption, retired permission codes, and stale incompatible roles all fail closed rather
    /// than being copied into a new access token or returned by <c>/identity/me</c>.
    /// </summary>
    public static Result<IReadOnlyList<string>> For(
        User user,
        Role? role,
        IPermissionRegistry permissionRegistry)
    {
        if (!Enum.IsDefined(user.UserType)
            || role is null
            || role.Id != user.RoleId
            || !Enum.IsDefined(role.CompatibleUserType)
            || role.CompatibleUserType != user.UserType)
        {
            return InvalidRoleConfiguration;
        }

        var validation = RolePermissionValidator.Validate(
            role.Permissions.ToList(),
            role.CompatibleUserType,
            permissionRegistry);
        if (validation.IsFailure)
            return InvalidRoleConfiguration;

        if (user.MfaRequired && !user.MfaEnabled)
            return Array.Empty<string>();

        return role.Permissions.ToList();
    }
}
