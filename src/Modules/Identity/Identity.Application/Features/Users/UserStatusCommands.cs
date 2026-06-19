using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Identity.Application.Features.Users;

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

// --- Resend invitation ----------------------------------------------------

public sealed record ResendInvitationCommand(Guid Id) : ICommand;

public sealed class ResendInvitationCommandHandler(
    IIdentityDbContext db,
    IInvitationNotifier invitationNotifier,
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
        var token = Guid.NewGuid();
        var result = user.ResendInvitation(token, now.AddHours(_options.InvitationExpiryHours), now);
        if (result.IsFailure)
            return result.Error;

        await db.SaveChangesAsync(cancellationToken);
        await invitationNotifier.SendInvitationAsync(user.Email.Value, user.DisplayName, user.Id, token, cancellationToken);
        return Result.Success();
    }
}
