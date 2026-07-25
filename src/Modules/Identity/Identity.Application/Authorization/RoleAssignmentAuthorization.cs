using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Authorization;

/// <summary>
/// Authorizes direct role delegation. The assign-role capability permits the operation, while the
/// permission ceiling prevents a caller from delegating capabilities they do not hold themselves.
/// </summary>
internal static class RoleAssignmentAuthorization
{
    private static readonly Error LiveAccessDenied = Error.Forbidden(
        "Your live account no longer permits this access-management action.",
        "Identity.User.AccessManagementForbidden");

    public static Result EnsureCanAssignRole(IUserContext userContext) =>
        userContext.HasPermission(IdentityPermissions.Users.AssignRole)
            ? Result.Success()
            : Error.Forbidden(
                "Assigning a role requires permission to assign roles.",
                "Identity.User.AssignRoleForbidden");

    public static Result EnsureCanChangeAccountType(IUserContext userContext) =>
        userContext.HasPermission(IdentityPermissions.Users.ChangeAccountType)
            ? Result.Success()
            : Error.Forbidden(
                "Changing an account type requires explicit permission.",
                "Identity.User.ChangeAccountTypeForbidden");

    public static Result EnsureLivePermission(Role actorRole, string permission, string errorCode) =>
        actorRole.HasPermission(permission)
            ? Result.Success()
            : Error.Forbidden(
                "Your current role no longer permits this access change.",
                errorCode);

    /// <summary>
    /// Revalidates the caller after the serialized access-management lock is acquired. This closes
    /// the window where a request passes JWT authorization, waits behind a demotion, and would
    /// otherwise continue executing with stale Administrator authority.
    /// </summary>
    public static async Task<Result<Role>> GetLiveActorRoleAsync(
        IIdentityDbContext db,
        IUserContext userContext,
        IPermissionRegistry permissions,
        string requiredPermission,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } actorId ||
            !userContext.HasPermission(requiredPermission))
        {
            return LiveAccessDenied;
        }

        var actor = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == actorId, cancellationToken);
        if (actor is null ||
            actor.Status != UserStatus.Active ||
            actor.IsLockedOut(now) ||
            actor.LoginEmailReleased ||
            actor.ExternalReferenceId is not null ||
            actor.UserType != BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator ||
            actor.UserType != userContext.UserType)
        {
            return LiveAccessDenied;
        }

        var actorRole = await db.Roles.AsNoTracking()
            .FirstOrDefaultAsync(role => role.Id == actor.RoleId, cancellationToken);
        if (actorRole is null ||
            actorRole.CompatibleUserType != actor.UserType ||
            RolePermissionValidator.Validate(
                actorRole.Permissions.ToList(),
                actor.UserType,
                permissions).IsFailure ||
            !actorRole.HasPermission(requiredPermission))
        {
            return LiveAccessDenied;
        }

        return actorRole;
    }

    public static Result EnsureWithinPermissionCeiling(IUserContext userContext, Role targetRole) =>
        targetRole.Permissions.All(userContext.HasPermission)
            ? Result.Success()
            : Error.Forbidden(
                "You cannot assign a role that grants permissions you do not hold.",
                "Identity.User.PermissionDelegationForbidden");

    public static Result EnsureWithinPermissionCeiling(
        IUserContext userContext,
        Role actorRole,
        Role managedRole,
        bool isCurrentRole)
    {
        var withinClaimCeiling = managedRole.Permissions.All(userContext.HasPermission);
        var withinLiveCeiling = managedRole.Permissions.All(actorRole.HasPermission);
        if (withinClaimCeiling && withinLiveCeiling)
            return Result.Success();

        return isCurrentRole
            ? Error.Forbidden(
                "You cannot manage a user whose current role grants permissions you do not hold.",
                "Identity.User.ManagedRoleForbidden")
            : Error.Forbidden(
                "You cannot assign a role that grants permissions you do not hold.",
                "Identity.User.PermissionDelegationForbidden");
    }
}
