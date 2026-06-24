using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Identity.Application.Features.Users;

/// <summary>Shared lifecycle guards used by deactivate/suspend.</summary>
internal static class UserLifecycleGuards
{
    /// <summary>
    /// True when blocking <paramref name="user"/> would leave no active System Administrator. The
    /// system must always retain at least one administrator who can sign in.
    /// </summary>
    public static async Task<bool> IsLastActiveAdminAsync(IIdentityDbContext db, Domain.Users.User user, CancellationToken ct)
    {
        if (user.UserType != UserType.SystemAdministrator || user.Status != UserStatus.Active)
            return false;

        var otherActiveAdmins = await db.Users.CountAsync(u =>
            u.Id != user.Id && u.UserType == UserType.SystemAdministrator && u.Status == UserStatus.Active, ct);

        return otherActiveAdmins == 0;
    }
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

        var result = user.Lock(timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

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

        var result = user.Unlock(timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

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

        if (await UserLifecycleGuards.IsLastActiveAdminAsync(db, user, cancellationToken))
            return Error.Conflict("Cannot deactivate the last active System Administrator.", "Identity.User.LastAdmin");

        var now = timeProvider.GetUtcNow();
        var result = user.Deactivate(now);
        if (result.IsFailure)
            return result.Error;

        var sessions = await db.Sessions
            .Where(s => s.UserId == user.Id && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
            session.Revoke(now);

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

        if (await UserLifecycleGuards.IsLastActiveAdminAsync(db, user, cancellationToken))
            return Error.Conflict("Cannot suspend the last active System Administrator.", "Identity.User.LastAdmin");

        var now = timeProvider.GetUtcNow();
        var result = user.Suspend(now);
        if (result.IsFailure)
            return result.Error;

        // Suspension blocks access immediately: revoke every active session.
        var sessions = await db.Sessions
            .Where(s => s.UserId == user.Id && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
            session.Revoke(now);

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
    IOptions<IdentityModuleOptions> options)
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

        await db.SaveChangesAsync(cancellationToken);

        if (invitation is not null)
            await invitationNotifier.SendInvitationAsync(user.Email.Value, user.DisplayName, user.Id, invitation.Value, cancellationToken);

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
    IOptions<IdentityModuleOptions> options)
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

        await db.SaveChangesAsync(cancellationToken);
        await invitationNotifier.SendInvitationAsync(user.Email.Value, user.DisplayName, user.Id, token.Value, cancellationToken);
        return Result.Success();
    }
}
