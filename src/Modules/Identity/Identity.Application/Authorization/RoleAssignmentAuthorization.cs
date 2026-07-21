using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Domain.Results;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;

namespace Identity.Application.Authorization;

/// <summary>
/// Authorizes direct role delegation. The assign-role capability permits the operation, while the
/// permission ceiling prevents a caller from delegating capabilities they do not hold themselves.
/// </summary>
internal static class RoleAssignmentAuthorization
{
    public static Result EnsureCanAssignRole(IUserContext userContext) =>
        userContext.HasPermission(IdentityPermissions.Users.AssignRole)
            ? Result.Success()
            : Error.Forbidden(
                "Assigning a role requires permission to assign roles.",
                "Identity.User.AssignRoleForbidden");

    public static Result EnsureWithinPermissionCeiling(IUserContext userContext, Role targetRole) =>
        targetRole.Permissions.All(userContext.HasPermission)
            ? Result.Success()
            : Error.Forbidden(
                "You cannot assign a role that grants permissions you do not hold.",
                "Identity.User.PermissionDelegationForbidden");
}
