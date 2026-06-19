using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Auth;

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : ICommand;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}

public sealed class ChangePasswordCommandHandler(
    IIdentityDbContext db,
    ICurrentUser currentUser,
    IPasswordHasher passwordHasher,
    TimeProvider timeProvider)
    : ICommandHandler<ChangePasswordCommand>
{
    public async Task<Result> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Error.Unauthorized();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return Error.NotFound("Account not found.", "Identity.User.NotFound");

        if (user.PasswordHash is null || !passwordHasher.Verify(user.PasswordHash, request.CurrentPassword))
            return Error.Validation("Current password is incorrect.", "Identity.Auth.WrongPassword");

        var hash = passwordHasher.Hash(request.NewPassword);
        var result = user.ChangePassword(hash, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        // Revoke all existing sessions so other devices must re-authenticate.
        var sessions = await db.Sessions
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
            session.Revoke(timeProvider.GetUtcNow());

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
