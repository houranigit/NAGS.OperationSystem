using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Identity.Contracts;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Application.Features.Users;

/// <summary>Shared lifecycle guards used by access-blocking account actions.</summary>
internal static class UserLifecycleGuards
{
    /// <summary>
    /// True when blocking <paramref name="user"/> would leave no System Administrator who can
    /// sign in. Locked, released, passwordless, and non-active accounts do not satisfy this
    /// break-glass invariant.
    /// </summary>
    public static async Task<bool> IsLastSignInCapableAdminAsync(
        IIdentityDbContext db,
        Domain.Users.User user,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!CanSignInAsAdmin(user, now))
            return false;

        var otherSignInCapableAdmins = await db.Users.AnyAsync(u =>
            u.Id != user.Id
            && u.UserType == UserType.SystemAdministrator
            && u.Status == UserStatus.Active
            && u.PasswordHash != null
            && !u.LoginEmailReleased
            && (u.LockoutEndUtc == null || u.LockoutEndUtc <= now), ct);

        return !otherSignInCapableAdmins;
    }

    private static bool CanSignInAsAdmin(Domain.Users.User user, DateTimeOffset now) =>
        user.UserType == UserType.SystemAdministrator
        && user.Status == UserStatus.Active
        && user.PasswordHash is not null
        && !user.LoginEmailReleased
        && !user.IsLockedOut(now);
}

// --- Lock -----------------------------------------------------------------

public sealed record LockUserCommand(Guid Id) : ICommand;

public sealed class LockUserCommandHandler(IIdentityDbContext db, ICurrentUser currentUser, TimeProvider timeProvider)
    : ICommandHandler<LockUserCommand>
{
    public async Task<Result> Handle(LockUserCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId == request.Id)
            return Error.Conflict("You cannot lock your own account.", "Identity.User.CannotLockSelf");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);
        if (user is null)
            return Error.NotFound("User not found.", "Identity.User.NotFound");

        var now = timeProvider.GetUtcNow();
        if (await UserLifecycleGuards.IsLastSignInCapableAdminAsync(db, user, now, cancellationToken))
            return Error.Conflict("Cannot lock the last sign-in-capable System Administrator.", "Identity.User.LastAdmin");

        var result = user.Lock(now);
        if (result.IsFailure)
            return result.Error;

        var sessions = await db.Sessions
            .Where(s => s.UserId == user.Id && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
            session.Revoke(now);

        if (user.ExternalReferenceId is { } externalReferenceId)
        {
            db.Enqueue(new PortalUserDeactivated
            {
                ExternalReferenceId = externalReferenceId,
                UserId = user.Id,
                UserType = user.UserType,
                ReleaseEmail = false
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// --- Unlock ---------------------------------------------------------------

public sealed record UnlockUserCommand(Guid Id) : ICommand;

public sealed class UnlockUserCommandHandler(IIdentityDbContext db, TimeProvider timeProvider)
    : ICommandHandler<UnlockUserCommand>
{
    public async Task<Result> Handle(UnlockUserCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);
        if (user is null)
            return Error.NotFound("User not found.", "Identity.User.NotFound");

        var now = timeProvider.GetUtcNow();
        var result = user.Unlock(now);
        if (result.IsFailure)
            return result.Error;

        if (user.ExternalReferenceId is { } externalReferenceId &&
            user.Status is UserStatus.Active or UserStatus.Invited)
        {
            db.Enqueue(new PortalUserAccessRestored
            {
                ExternalReferenceId = externalReferenceId,
                UserId = user.Id,
                UserType = user.UserType,
                IsActivated = user.Status == UserStatus.Active
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// --- Deactivate -----------------------------------------------------------

public sealed record DeactivateUserCommand(Guid Id) : ICommand;

public sealed class DeactivateUserCommandHandler(IIdentityDbContext db, ICurrentUser currentUser, TimeProvider timeProvider)
    : ICommandHandler<DeactivateUserCommand>
{
    public async Task<Result> Handle(DeactivateUserCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId == request.Id)
            return Error.Conflict("You cannot deactivate your own account.", "Identity.User.CannotDeactivateSelf");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);
        if (user is null)
            return Error.NotFound("User not found.", "Identity.User.NotFound");

        var now = timeProvider.GetUtcNow();
        if (await UserLifecycleGuards.IsLastSignInCapableAdminAsync(db, user, now, cancellationToken))
            return Error.Conflict("Cannot deactivate the last sign-in-capable System Administrator.", "Identity.User.LastAdmin");

        var result = user.Deactivate(now);
        if (result.IsFailure)
            return result.Error;

        var sessions = await db.Sessions
            .Where(s => s.UserId == user.Id && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
            session.Revoke(now);

        if (user.ExternalReferenceId is { } externalReferenceId)
        {
            db.Enqueue(new PortalUserDeactivated
            {
                ExternalReferenceId = externalReferenceId,
                UserId = user.Id,
                UserType = user.UserType,
                ReleaseEmail = false
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// --- Suspend --------------------------------------------------------------

public sealed record SuspendUserCommand(Guid Id) : ICommand;

public sealed class SuspendUserCommandHandler(IIdentityDbContext db, ICurrentUser currentUser, TimeProvider timeProvider)
    : ICommandHandler<SuspendUserCommand>
{
    public async Task<Result> Handle(SuspendUserCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId == request.Id)
            return Error.Conflict("You cannot suspend your own account.", "Identity.User.CannotSuspendSelf");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);
        if (user is null)
            return Error.NotFound("User not found.", "Identity.User.NotFound");

        var now = timeProvider.GetUtcNow();
        if (await UserLifecycleGuards.IsLastSignInCapableAdminAsync(db, user, now, cancellationToken))
            return Error.Conflict("Cannot suspend the last sign-in-capable System Administrator.", "Identity.User.LastAdmin");

        var result = user.Suspend(now);
        if (result.IsFailure)
            return result.Error;

        // Suspension blocks access immediately: revoke every active session.
        var sessions = await db.Sessions
            .Where(s => s.UserId == user.Id && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
            session.Revoke(now);

        if (user.ExternalReferenceId is { } externalReferenceId)
        {
            db.Enqueue(new PortalUserDeactivated
            {
                ExternalReferenceId = externalReferenceId,
                UserId = user.Id,
                UserType = user.UserType,
                ReleaseEmail = false
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// --- Restore access -------------------------------------------------------

public sealed record RestoreAccessCommand(Guid Id) : ICommand;

public sealed class RestoreAccessCommandHandler(
    IIdentityDbContext db,
    IInvitationNotifier invitationNotifier,
    ITokenService tokenService,
    TimeProvider timeProvider,
    IOptions<IdentityModuleOptions> options,
    ILogger<RestoreAccessCommandHandler> logger)
    : ICommandHandler<RestoreAccessCommand>
{
    private readonly IdentityModuleOptions _options = options.Value;

    public async Task<Result> Handle(RestoreAccessCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);
        if (user is null)
            return Error.NotFound("User not found.", "Identity.User.NotFound");

        var now = timeProvider.GetUtcNow();
        var result = user.RestoreAccess(now);
        if (result.IsFailure)
            return result.Error;

        // An account suspended before activation goes back to Invited; rotate and redeliver a fresh
        // invitation so the original (possibly stale) token cannot be used.
        SecureToken? invitation = null;
        if (result.Value == AccessRestoreOutcome.InvitationRequeued)
        {
            invitation = tokenService.CreateSecureToken();
            var requeue = user.ResendInvitation(invitation.Hash, now.AddHours(_options.InvitationExpiryHours), now);
            if (requeue.IsFailure)
                return requeue.Error;
        }

        if (user.ExternalReferenceId is { } externalReferenceId)
        {
            db.Enqueue(new PortalUserAccessRestored
            {
                ExternalReferenceId = externalReferenceId,
                UserId = user.Id,
                UserType = user.UserType,
                IsActivated = result.Value == AccessRestoreOutcome.Reactivated
            });
        }

        if (invitation is not null)
        {
            try
            {
                await invitationNotifier.SendInvitationAsync(user.Email.Value, user.DisplayName, user.Id, invitation.Value, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Invitation delivery failed while restoring access for user {UserId}.", user.Id);
                return Error.Failure("The invitation could not be queued. Access was not restored.", "Identity.User.InvitationDeliveryFailed");
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

// --- Resend invitation ----------------------------------------------------

public sealed record ResendInvitationCommand(Guid Id) : ICommand;

public sealed class ResendInvitationCommandHandler(
    IIdentityDbContext db,
    IInvitationNotifier invitationNotifier,
    ITokenService tokenService,
    TimeProvider timeProvider,
    IOptions<IdentityModuleOptions> options,
    ILogger<ResendInvitationCommandHandler> logger)
    : ICommandHandler<ResendInvitationCommand>
{
    private readonly IdentityModuleOptions _options = options.Value;

    public async Task<Result> Handle(ResendInvitationCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);
        if (user is null)
            return Error.NotFound("User not found.", "Identity.User.NotFound");

        var now = timeProvider.GetUtcNow();
        var token = tokenService.CreateSecureToken();
        var result = user.ResendInvitation(token.Hash, now.AddHours(_options.InvitationExpiryHours), now);
        if (result.IsFailure)
            return result.Error;

        try
        {
            await invitationNotifier.SendInvitationAsync(user.Email.Value, user.DisplayName, user.Id, token.Value, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Invitation delivery failed while resending invitation for user {UserId}.", user.Id);
            return Error.Failure("The invitation could not be queued. The existing invitation remains valid.", "Identity.User.InvitationDeliveryFailed");
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
