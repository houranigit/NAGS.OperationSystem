using Identity.Domain.Roles;
using Identity.Domain.Users;

namespace Identity.Application.Authorization;

internal static class EffectiveUserPermissions
{
    public static IReadOnlyList<string> For(User user, Role? role)
    {
        if (user.MfaRequired && !user.MfaEnabled)
            return [];

        return role?.Permissions.ToList() ?? [];
    }
}
