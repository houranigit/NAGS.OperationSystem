using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Application.Contracts;
using Identity.Domain.Authorization;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Application.Features.Users;

/// <summary>
/// Direct user creation supports System Administrator and Viewer Only roles. Station Staff and
/// Customer Contact accounts are provisioned from their MasterData record via portal access.
/// </summary>
public sealed record InviteUserCommand(string Email, string DisplayName, Guid? RoleId = null) : ICommand<InvitedUserDto>;

public sealed class InviteUserCommandValidator : AbstractValidator<InviteUserCommand>
{
    public InviteUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.RoleId).Must(id => id is null || id.Value != Guid.Empty)
            .WithMessage("A valid role is required.");
    }
}

public sealed class InviteUserCommandHandler(
    IIdentityDbContext db,
    IUserContext userContext,
    IPermissionRegistry permissions,
    IInvitationNotifier invitationNotifier,
    ITokenService tokenService,
    TimeProvider timeProvider,
    IOptions<IdentityModuleOptions> options,
    ILogger<InviteUserCommandHandler> logger)
    : ICommandHandler<InviteUserCommand, InvitedUserDto>
{
    private readonly IdentityModuleOptions _options = options.Value;

    public async Task<Result<InvitedUserDto>> Handle(InviteUserCommand request, CancellationToken cancellationToken)
    {
        if (!userContext.HasPermission(IdentityPermissions.Users.Invite))
        {
            return Error.Forbidden(
                "Inviting a user requires permission to invite users.",
                "Identity.User.InviteForbidden");
        }

        // Inviting a user always assigns a role. The endpoint's invite permission is therefore not
        // sufficient on its own, even when the caller omits RoleId and the protected system role is
        // selected as the compatibility default.
        var roleAssignmentAccess = RoleAssignmentAuthorization.EnsureCanAssignRole(userContext);
        if (roleAssignmentAccess.IsFailure)
            return roleAssignmentAccess.Error;

        try
        {
            await using var transaction =
                await db.BeginAccessManagementTransactionAsync(cancellationToken);

            var now = timeProvider.GetUtcNow();
            var liveActorRole = await RoleAssignmentAuthorization.GetLiveActorRoleAsync(
                db,
                userContext,
                permissions,
                IdentityPermissions.Users.Invite,
                now,
                cancellationToken);
            if (liveActorRole.IsFailure)
            {
                return Error.Forbidden(
                    liveActorRole.Error.Description,
                    "Identity.User.InviteForbidden");
            }

            var liveAssignmentAccess = RoleAssignmentAuthorization.EnsureLivePermission(
                liveActorRole.Value,
                IdentityPermissions.Users.AssignRole,
                "Identity.User.AssignRoleForbidden");
            if (liveAssignmentAccess.IsFailure)
                return liveAssignmentAccess.Error;

            var emailResult = Email.Create(request.Email);
            if (emailResult.IsFailure)
                return emailResult.Error;

            var email = emailResult.Value;
            var emailValue = email.Value;

            // Only an active login email participates in uniqueness; released emails are reusable.
            var emailTaken = await db.Users.AnyAsync(
                u => u.Email.Value == emailValue && !u.LoginEmailReleased,
                cancellationToken);
            if (emailTaken)
                return Error.Conflict("A user with this email already exists.", "Identity.User.DuplicateEmail");

            // The selected role is authoritative for the account's initial direct type. Omitting
            // RoleId keeps the legacy behavior of selecting the protected System Administrator role.
            var role = request.RoleId is { } roleId
                ? await db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken)
                : await db.Roles.FirstOrDefaultAsync(r => r.IsSystem, cancellationToken);
            if (role is null)
            {
                return request.RoleId is { }
                    ? Error.Validation("The selected role does not exist.", "Identity.User.RoleNotFound")
                    : Error.Failure("The protected System Administrator role is not available.", "Identity.User.NoAdminRole");
            }

            if (!role.CompatibleUserType.IsDirectlyProvisioned())
            {
                return Error.Conflict(
                    $"Role '{role.Name}' is not compatible with direct invitations.",
                    "Identity.User.IncompatibleRole");
            }

            var roleConfiguration = RolePermissionValidator.Validate(
                role.Permissions.ToList(),
                role.CompatibleUserType,
                permissions);
            if (roleConfiguration.IsFailure)
                return roleConfiguration.Error;

            // Prevent permission escalation through delegation. Both the token and the actor's
            // live role bound the selected role, including the legacy default.
            var delegationAccess = RoleAssignmentAuthorization.EnsureWithinPermissionCeiling(
                userContext,
                liveActorRole.Value,
                role,
                isCurrentRole: false);
            if (delegationAccess.IsFailure)
                return delegationAccess.Error;

            var token = tokenService.CreateSecureToken();
            var expiry = now.AddHours(_options.InvitationExpiryHours);

            var userResult = User.Invite(
                email,
                request.DisplayName,
                role.Id,
                token.Hash,
                expiry,
                now,
                role.CompatibleUserType);
            if (userResult.IsFailure)
                return userResult.Error;

            var user = userResult.Value;
            db.Users.Add(user);

            // Queue the credential while the access lock is still held. A role update cannot rotate
            // the invitation and queue a replacement before this original message is queued.
            var deliveryStatus = "Queued";
            try
            {
                await invitationNotifier.SendInvitationAsync(
                    email.Value,
                    request.DisplayName,
                    user.Id,
                    token.Value,
                    cancellationToken);
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException and
                not DbUpdateConcurrencyException)
            {
                logger.LogError(ex, "Invitation delivery failed for direct user {UserId}.", user.Id);
                deliveryStatus = "Failed";
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new InvitedUserDto(user.Id, email.Value, deliveryStatus);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }
    }
}
