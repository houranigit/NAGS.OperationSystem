using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Application.Features.Roles;

public sealed record UpdateRolePermissionsCommand(Guid Id, IReadOnlyList<string> Permissions) : ICommand;

public sealed class UpdateRolePermissionsCommandValidator : AbstractValidator<UpdateRolePermissionsCommand>
{
    public UpdateRolePermissionsCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Permissions).NotNull();
    }
}

public sealed class UpdateRolePermissionsCommandHandler(
    IIdentityDbContext db,
    IUserContext userContext,
    IPermissionRegistry permissions,
    IInvitationNotifier invitationNotifier,
    ITokenService tokenService,
    TimeProvider timeProvider,
    IOptions<IdentityModuleOptions> options,
    ILogger<UpdateRolePermissionsCommandHandler> logger)
    : ICommandHandler<UpdateRolePermissionsCommand>
{
    private readonly IdentityModuleOptions _options = options.Value;

    public async Task<Result> Handle(UpdateRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction =
                await db.BeginAccessManagementTransactionAsync(cancellationToken);

            var now = timeProvider.GetUtcNow();
            var access = await RoleAssignmentAuthorization.GetLiveActorRoleAsync(
                db,
                userContext,
                permissions,
                IdentityPermissions.Roles.ManagePermissions,
                now,
                cancellationToken);
            if (access.IsFailure)
                return access.Error;

            var role = await db.Roles.FirstOrDefaultAsync(
                candidate => candidate.Id == request.Id,
                cancellationToken);
            if (role is null)
                return Error.NotFound("Role not found.", "Identity.Role.NotFound");

            if (role.IsSystem)
            {
                return Error.Conflict(
                    "System role permissions cannot be modified.",
                    "Identity.Role.SystemProtected");
            }

            if (access.Value.Id == role.Id)
            {
                return Error.Conflict("You cannot modify permissions for your own role.", "Identity.Role.CannotModifyOwnPermissions");
            }

            var currentCeiling = RoleAssignmentAuthorization.EnsureWithinPermissionCeiling(
                userContext,
                access.Value,
                role,
                isCurrentRole: true);
            if (currentCeiling.IsFailure)
                return currentCeiling.Error;

            var permissionCheck = RolePermissionValidator.Validate(
                request.Permissions,
                role.CompatibleUserType,
                permissions);
            if (permissionCheck.IsFailure)
                return permissionCheck.Error;

            var requestedCeiling = RoleMutationAuthorization.EnsureRequestedPermissionsWithinCeiling(
                userContext,
                access.Value,
                request.Permissions);
            if (requestedCeiling.IsFailure)
                return requestedCeiling.Error;

            var permissionsChanged = !role.Permissions
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(request.Permissions);
            if (!permissionsChanged)
            {
                await transaction.CommitAsync(cancellationToken);
                return Result.Success();
            }

            var result = role.SetPermissions(request.Permissions, now);
            if (result.IsFailure)
                return result.Error;

            try
            {
                var invalidation = await RoleHolderAccessInvalidation.InvalidateAsync(
                    db,
                    [role.Id],
                    invitationNotifier,
                    tokenService,
                    _options.InvitationExpiryHours,
                    now,
                    cancellationToken);
                if (invalidation.IsFailure)
                    return invalidation.Error;
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException and
                not DbUpdateConcurrencyException)
            {
                logger.LogError(
                    ex,
                    "Replacement invitation delivery failed while updating permissions for role {RoleId}.",
                    role.Id);
                return Error.Failure(
                    "A replacement invitation could not be queued. Role permissions were not changed.",
                    "Identity.User.InvitationDeliveryFailed");
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }
    }
}

internal static class RoleMutationAuthorization
{
    public static Result EnsureRequestedPermissionsWithinCeiling(
        IUserContext userContext,
        Role actorRole,
        IReadOnlyCollection<string> requestedPermissions) =>
        requestedPermissions.All(userContext.HasPermission) &&
        requestedPermissions.All(actorRole.HasPermission)
            ? Result.Success()
            : Error.Forbidden(
                "You cannot grant permissions you do not hold.",
                "Identity.User.PermissionDelegationForbidden");
}
