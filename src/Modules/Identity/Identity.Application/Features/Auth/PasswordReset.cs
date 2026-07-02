using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Identity.Application.Features.Auth;

// --- Forgot password ------------------------------------------------------

/// <summary>
/// Starts a password reset. Always succeeds from the caller's perspective (non-enumerating): the
/// response never reveals whether the email belongs to an account. A reset email is sent only when
/// an active account exists.
/// </summary>
public sealed record ForgotPasswordCommand(string Email) : ICommand;

public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator() => RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
}

public sealed class ForgotPasswordCommandHandler(
    IIdentityDbContext db,
    IPasswordResetNotifier notifier,
    ITokenService tokenService,
    TimeProvider timeProvider,
    IOptions<IdentityModuleOptions> options)
    : ICommandHandler<ForgotPasswordCommand>
{
    private readonly IdentityModuleOptions _options = options.Value;

    public async Task<Result> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure)
            return Result.Success(); // Do not reveal validation differences.

        var emailValue = emailResult.Value.Value;
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Email.Value == emailValue && !u.LoginEmailReleased, cancellationToken);

        if (user is null || user.Status != UserStatus.Active)
            return Result.Success();

        var now = timeProvider.GetUtcNow();
        var token = tokenService.CreateSecureToken();
        var request2 = user.RequestPasswordReset(token.Hash, now.AddHours(_options.PasswordResetExpiryHours), now);
        if (request2.IsFailure)
            return Result.Success();

        await db.SaveChangesAsync(cancellationToken);
        await notifier.SendPasswordResetAsync(user.Email.Value, user.DisplayName, user.Id, token.Value, cancellationToken);
        return Result.Success();
    }
}

// --- Reset password -------------------------------------------------------

public sealed record ResetPasswordCommand(string Token, string NewPassword) : ICommand;

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}

public sealed class ResetPasswordCommandHandler(
    IIdentityDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    TimeProvider timeProvider)
    : ICommandHandler<ResetPasswordCommand>
{
    public async Task<Result> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var invalid = Error.Validation("The reset link is invalid or has expired.", "Identity.Auth.InvalidReset");

        var tokenHash = tokenService.HashToken(request.Token);
        var user = await db.Users.FirstOrDefaultAsync(u => u.PasswordResetToken == tokenHash, cancellationToken);
        if (user is null)
            return invalid;

        var now = timeProvider.GetUtcNow();
        if (user.ValidatePasswordReset(tokenHash, now).IsFailure)
            return invalid;

        var result = user.ResetPassword(tokenHash, passwordHasher.Hash(request.NewPassword), now);
        if (result.IsFailure)
            return invalid;

        // A password reset invalidates all existing sessions.
        var sessions = await db.Sessions
            .Where(s => s.UserId == user.Id && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
            session.Revoke(now);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
