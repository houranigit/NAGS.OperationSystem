using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Contracts;
using Identity.Domain.Authorization;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Application.Features.Users;

/// <summary>
/// Changes a user's role. The selected role is authoritative for the resulting account type; only
/// the two direct account types may cross that boundary.
/// </summary>
public sealed record AssignRoleCommand(Guid UserId, Guid RoleId, byte[] RowVersion) : ICommand;

public sealed class AssignRoleCommandValidator : AbstractValidator<AssignRoleCommand>
{
    public AssignRoleCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class AssignRoleCommandHandler(
    IIdentityDbContext db,
    IUserContext userContext,
    IPermissionRegistry permissions,
    IInvitationNotifier invitationNotifier,
    ITokenService tokenService,
    TimeProvider timeProvider,
    IOptions<IdentityModuleOptions> options,
    ILogger<AssignRoleCommandHandler> logger)
    : ICommandHandler<AssignRoleCommand>
{
    private readonly IdentityModuleOptions _options = options.Value;

    public async Task<Result> Handle(AssignRoleCommand request, CancellationToken cancellationToken)
    {
        var claimedAssignmentAccess = RoleAssignmentAuthorization.EnsureCanAssignRole(userContext);
        if (claimedAssignmentAccess.IsFailure)
            return claimedAssignmentAccess.Error;

        if (userContext.UserId is not { } actorId)
            return Error.Forbidden(
                "Assigning a role requires an authenticated administrator.",
                "Identity.User.AssignRoleForbidden");

        if (actorId == request.UserId)
            return Error.Conflict(
                "You cannot change your own role or account type.",
                "Identity.User.CannotAssignRoleSelf");

        try
        {
            await using var transaction =
                await db.BeginAccessManagementTransactionAsync(cancellationToken);

            var now = timeProvider.GetUtcNow();
            var liveActorRole = await RoleAssignmentAuthorization.GetLiveActorRoleAsync(
                db,
                userContext,
                permissions,
                IdentityPermissions.Users.AssignRole,
                now,
                cancellationToken);
            if (liveActorRole.IsFailure)
                return Error.Forbidden(
                    liveActorRole.Error.Description,
                    "Identity.User.AssignRoleForbidden");

            var actorRole = liveActorRole.Value;

            var user = await db.Users
                .FirstOrDefaultAsync(candidate => candidate.Id == request.UserId, cancellationToken);
            if (user is null)
                return Error.NotFound("User not found.", "Identity.User.NotFound");

            var currentRole = await db.Roles
                .FirstOrDefaultAsync(role => role.Id == user.RoleId, cancellationToken);
            if (currentRole is null)
            {
                return Error.Conflict(
                    "The user's current role no longer exists.",
                    "Identity.User.CurrentRoleNotFound");
            }

            if (currentRole.CompatibleUserType != user.UserType)
            {
                return Error.Conflict(
                    "The user's current role is incompatible with the stored account type.",
                    "Identity.User.CurrentRoleIncompatible");
            }

            var currentRoleCheck = RolePermissionValidator.Validate(
                currentRole.Permissions.ToList(),
                currentRole.CompatibleUserType,
                permissions);
            if (currentRoleCheck.IsFailure)
            {
                return Error.Conflict(
                    "The user's current role has an invalid permission configuration.",
                    "Identity.User.CurrentRoleInvalid");
            }

            var targetRole = currentRole.Id == request.RoleId
                ? currentRole
                : await db.Roles.FirstOrDefaultAsync(
                    role => role.Id == request.RoleId,
                    cancellationToken);
            if (targetRole is null)
            {
                return Error.Validation(
                    "The selected role does not exist.",
                    "Identity.User.RoleNotFound");
            }

            var targetRoleCheck = RolePermissionValidator.Validate(
                targetRole.Permissions.ToList(),
                targetRole.CompatibleUserType,
                permissions);
            if (targetRoleCheck.IsFailure)
            {
                return Error.Conflict(
                    "The selected role has an invalid permission configuration.",
                    "Identity.User.SelectedRoleInvalid");
            }

            var currentCeiling = RoleAssignmentAuthorization.EnsureWithinPermissionCeiling(
                userContext,
                actorRole,
                currentRole,
                isCurrentRole: true);
            if (currentCeiling.IsFailure)
                return currentCeiling.Error;

            var targetCeiling = RoleAssignmentAuthorization.EnsureWithinPermissionCeiling(
                userContext,
                actorRole,
                targetRole,
                isCurrentRole: false);
            if (targetCeiling.IsFailure)
                return targetCeiling.Error;

            var changesAccountType = targetRole.CompatibleUserType != user.UserType;
            if (changesAccountType)
            {
                var claimedTypeAccess =
                    RoleAssignmentAuthorization.EnsureCanChangeAccountType(userContext);
                if (claimedTypeAccess.IsFailure)
                    return claimedTypeAccess.Error;

                var liveTypeAccess = RoleAssignmentAuthorization.EnsureLivePermission(
                    actorRole,
                    IdentityPermissions.Users.ChangeAccountType,
                    "Identity.User.ChangeAccountTypeForbidden");
                if (liveTypeAccess.IsFailure)
                    return liveTypeAccess.Error;
            }

            var leavesProtectedAdministratorRole =
                currentRole.IsSystem &&
                currentRole.CompatibleUserType == BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator &&
                (!targetRole.IsSystem ||
                 targetRole.CompatibleUserType != BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator);
            if (leavesProtectedAdministratorRole)
            {
                var removesLastLiveHolder =
                    await UserLifecycleGuards.IsLastLiveProtectedAdministratorAsync(
                        db,
                        user,
                        cancellationToken);
                var removesLastSignInCapableHolder =
                    await UserLifecycleGuards.IsLastSignInCapableAdminAsync(
                        db,
                        user,
                        now,
                        cancellationToken);
                if (removesLastLiveHolder || removesLastSignInCapableHolder)
                {
                    return Error.Conflict(
                        "Cannot remove access from the last protected System Administrator.",
                        "Identity.User.LastAdmin");
                }
            }

            db.SetOriginalRowVersion(user, request.RowVersion);

            var canceledPendingEmail = user.PendingEmail;
            var change = user.ChangeAccess(
                targetRole.Id,
                targetRole.CompatibleUserType,
                now);
            if (change.IsFailure)
                return change.Error;

            if (change.Value == AccessChangeOutcome.Unchanged)
            {
                await transaction.CommitAsync(cancellationToken);
                return Result.Success();
            }

            if (canceledPendingEmail is not null &&
                user.ExternalReferenceId is { } externalReferenceId &&
                user.UserType.RequiresExternalReference())
            {
                db.Enqueue(new PortalUserEmailChangeFailed
                {
                    ExternalReferenceId = externalReferenceId,
                    UserId = user.Id,
                    UserType = user.UserType,
                    Email = canceledPendingEmail,
                    Reason =
                        "Login email verification was canceled because the account's access changed. Request the email change again."
                });
            }

            // An invitation is a credential for the access configuration that existed when it was
            // issued. Rotate and re-deliver it inside this transaction so a retained Viewer link
            // can never activate an account after that account was promoted.
            SecureToken? invitation = null;
            if (user.Status == UserStatus.Invited)
            {
                invitation = tokenService.CreateSecureToken();
                var requeue = user.ResendInvitation(
                    invitation.Hash,
                    now.AddHours(_options.InvitationExpiryHours),
                    now);
                if (requeue.IsFailure)
                    return requeue.Error;
            }

            var sessions = await db.Sessions
                .Where(session =>
                    session.UserId == user.Id &&
                    session.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var session in sessions)
                session.Revoke(now);

            if (invitation is not null)
            {
                try
                {
                    await invitationNotifier.SendInvitationAsync(
                        user.Email.Value,
                        user.DisplayName,
                        user.Id,
                        invitation.Value,
                        cancellationToken);
                }
                catch (Exception ex) when (
                    ex is not OperationCanceledException and
                    not DbUpdateConcurrencyException)
                {
                    logger.LogError(
                        ex,
                        "Invitation delivery failed while changing access for invited user {UserId}.",
                        user.Id);
                    return Error.Failure(
                        "The replacement invitation could not be queued. Access was not changed.",
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
