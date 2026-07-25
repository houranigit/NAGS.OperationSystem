using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Application.Features.Roles;

/// <summary>
/// Atomically updates a role's profile and permission grant. This command backs the two-step role
/// editor; the narrower metadata-only and permissions-only commands remain available for callers
/// that hold just one of those capabilities.
/// </summary>
public sealed record UpdateRoleAndPermissionsCommand(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<string> Permissions) : ICommand;

public sealed class UpdateRoleAndPermissionsCommandValidator : AbstractValidator<UpdateRoleAndPermissionsCommand>
{
    public UpdateRoleAndPermissionsCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Permissions).NotNull();
    }
}

public sealed class UpdateRoleAndPermissionsCommandHandler(
    IIdentityDbContext db,
    IUserContext userContext,
    IPermissionRegistry permissions,
    IInvitationNotifier invitationNotifier,
    ITokenService tokenService,
    TimeProvider timeProvider,
    IOptions<IdentityModuleOptions> options,
    ILogger<UpdateRoleAndPermissionsCommandHandler> logger)
    : ICommandHandler<UpdateRoleAndPermissionsCommand>
{
    private readonly IdentityModuleOptions _options = options.Value;

    public async Task<Result> Handle(UpdateRoleAndPermissionsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction =
                await db.BeginAccessManagementTransactionAsync(cancellationToken);

            var now = timeProvider.GetUtcNow();
            var updateAccess = await RoleAssignmentAuthorization.GetLiveActorRoleAsync(
                db,
                userContext,
                permissions,
                IdentityPermissions.Roles.Update,
                now,
                cancellationToken);
            if (updateAccess.IsFailure)
                return updateAccess.Error;

            var permissionAccess = await RoleAssignmentAuthorization.GetLiveActorRoleAsync(
                db,
                userContext,
                permissions,
                IdentityPermissions.Roles.ManagePermissions,
                now,
                cancellationToken);
            if (permissionAccess.IsFailure)
                return permissionAccess.Error;

            var role = await db.Roles.FirstOrDefaultAsync(
                candidate => candidate.Id == request.Id,
                cancellationToken);
            if (role is null)
                return Error.NotFound("Role not found.", "Identity.Role.NotFound");

            if (role.IsSystem)
            {
                return Error.Conflict(
                    "System roles cannot be modified.",
                    "Identity.Role.SystemProtected");
            }

            var normalized = request.Name.Trim().ToUpperInvariant();
            var duplicate = await db.Roles.AnyAsync(
                candidate =>
                    candidate.NormalizedName == normalized &&
                    candidate.Id != role.Id,
                cancellationToken);
            if (duplicate)
            {
                return Error.Conflict(
                    "A role with this name already exists.",
                    "Identity.Role.DuplicateName");
            }

            var permissionCheck = RolePermissionValidator.Validate(
                request.Permissions,
                role.CompatibleUserType,
                permissions);
            if (permissionCheck.IsFailure)
                return permissionCheck.Error;

            var permissionsChanged = !role.Permissions.ToHashSet(StringComparer.Ordinal)
                .SetEquals(request.Permissions);

            if (permissionsChanged && updateAccess.Value.Id == role.Id)
            {
                return Error.Conflict("You cannot modify permissions for your own role.", "Identity.Role.CannotModifyOwnPermissions");
            }

            var currentCeiling = RoleAssignmentAuthorization.EnsureWithinPermissionCeiling(
                userContext,
                permissionAccess.Value,
                role,
                isCurrentRole: true);
            if (currentCeiling.IsFailure)
                return currentCeiling.Error;

            var requestedCeiling = RoleMutationAuthorization.EnsureRequestedPermissionsWithinCeiling(
                userContext,
                permissionAccess.Value,
                request.Permissions);
            if (requestedCeiling.IsFailure)
                return requestedCeiling.Error;

            var updateResult = role.Update(request.Name, request.Description, now);
            if (updateResult.IsFailure)
                return updateResult.Error;

            if (permissionsChanged)
            {
                var permissionResult = role.SetPermissions(request.Permissions, now);
                if (permissionResult.IsFailure)
                    return permissionResult.Error;

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
                        "Replacement invitation delivery failed while updating role {RoleId}.",
                        role.Id);
                    return Error.Failure(
                        "A replacement invitation could not be queued. The role was not changed.",
                        "Identity.User.InvitationDeliveryFailed");
                }
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
