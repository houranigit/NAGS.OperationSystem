using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Application.Contracts;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Identity.Application.Features.Auth;

// --- Login second step (MFA) ---------------------------------------------

public sealed record LoginMfaCommand(string MfaToken, string Code, string? IpAddress, string? UserAgent) : ICommand<AuthTokensDto>;

public sealed class LoginMfaCommandValidator : AbstractValidator<LoginMfaCommand>
{
    public LoginMfaCommandValidator()
    {
        RuleFor(x => x.MfaToken).NotEmpty();
        RuleFor(x => x.Code).NotEmpty();
    }
}

public sealed class LoginMfaCommandHandler(
    IIdentityDbContext db,
    ITokenService tokenService,
    IMfaService mfaService,
    IMfaSecretProtector secretProtector,
    TimeProvider timeProvider,
    IOptions<IdentityModuleOptions> options)
    : ICommandHandler<LoginMfaCommand, AuthTokensDto>
{
    private static readonly Error Invalid = Error.Unauthorized("Invalid or expired sign-in.", "Identity.Auth.InvalidMfa");
    private readonly IdentityModuleOptions _options = options.Value;

    public async Task<Result<AuthTokensDto>> Handle(LoginMfaCommand request, CancellationToken cancellationToken)
    {
        if (tokenService.ValidateMfaChallengeToken(request.MfaToken) is not { } challenge)
            return Invalid;

        var now = timeProvider.GetUtcNow();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == challenge.UserId, cancellationToken);
        if (user is null
            || user.Status != UserStatus.Active
            || user.SecurityStamp != challenge.SecurityStamp
            || user.IsLockedOut(now)
            || !user.MfaEnabled
            || user.MfaSecret is null)
            return Invalid;

        var secret = secretProtector.Unprotect(user.MfaSecret);
        var verified = mfaService.VerifyCode(secret, request.Code, now)
            || user.ConsumeRecoveryCode(tokenService.HashToken(request.Code.Trim()), now).IsSuccess;

        if (!verified)
        {
            var locked = user.RecordFailedSignIn(_options.MaxFailedSignInAttempts, TimeSpan.FromMinutes(_options.LockoutMinutes), now);
            if (locked)
                await AuthSessionRevocation.RevokeActiveSessionsAsync(db, user.Id, now, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            return Invalid;
        }

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == user.RoleId, cancellationToken);
        var permissions = EffectiveUserPermissions.For(user, role);

        user.RecordSuccessfulSignIn(now);

        var refresh = tokenService.CreateRefreshToken();
        var sessionResult = Domain.Sessions.UserSession.Issue(user.Id, refresh.Hash, refresh.ExpiresAtUtc, now, request.IpAddress, request.UserAgent);
        if (sessionResult.IsFailure)
            return sessionResult.Error;

        db.Sessions.Add(sessionResult.Value);
        var access = tokenService.CreateAccessToken(user, permissions, sessionResult.Value.Id);
        await db.SaveChangesAsync(cancellationToken);

        return new AuthTokensDto(access.Value, access.ExpiresAtUtc, refresh.Value, refresh.ExpiresAtUtc);
    }
}

// --- Enroll (current user) ------------------------------------------------

public sealed record EnrollMfaCommand : ICommand<MfaEnrollmentDto>;

public sealed class EnrollMfaCommandHandler(
    IIdentityDbContext db,
    ICurrentUser currentUser,
    IMfaService mfaService,
    IMfaSecretProtector secretProtector,
    TimeProvider timeProvider)
    : ICommandHandler<EnrollMfaCommand, MfaEnrollmentDto>
{
    public async Task<Result<MfaEnrollmentDto>> Handle(EnrollMfaCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Error.Unauthorized();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return Error.NotFound("Account not found.", "Identity.User.NotFound");

        var secret = mfaService.GenerateSecret();
        var begin = user.BeginMfaEnrollment(secretProtector.Protect(secret), timeProvider.GetUtcNow());
        if (begin.IsFailure)
            return begin.Error;

        await db.SaveChangesAsync(cancellationToken);
        return new MfaEnrollmentDto(secret, mfaService.BuildOtpAuthUri(secret, user.Email.Value));
    }
}

// --- Confirm enrollment (current user) ------------------------------------

public sealed record ConfirmMfaCommand(string Code) : ICommand<MfaRecoveryCodesDto>;

public sealed class ConfirmMfaCommandValidator : AbstractValidator<ConfirmMfaCommand>
{
    public ConfirmMfaCommandValidator() => RuleFor(x => x.Code).NotEmpty();
}

public sealed class ConfirmMfaCommandHandler(
    IIdentityDbContext db,
    ICurrentUser currentUser,
    ITokenService tokenService,
    IMfaService mfaService,
    IMfaSecretProtector secretProtector,
    TimeProvider timeProvider)
    : ICommandHandler<ConfirmMfaCommand, MfaRecoveryCodesDto>
{
    public async Task<Result<MfaRecoveryCodesDto>> Handle(ConfirmMfaCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Error.Unauthorized();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return Error.NotFound("Account not found.", "Identity.User.NotFound");

        if (user.MfaSecret is null)
            return Error.Conflict("Start MFA enrollment before confirming.", "Identity.User.NoPendingMfa");

        var now = timeProvider.GetUtcNow();
        var secret = secretProtector.Unprotect(user.MfaSecret);
        if (!mfaService.VerifyCode(secret, request.Code, now))
            return Error.Validation("That code is not valid. Try again with a fresh code.", "Identity.User.InvalidMfaCode");

        var recoveryCodes = mfaService.GenerateRecoveryCodes(10);
        var hashes = recoveryCodes.Select(tokenService.HashToken).ToList();

        var confirm = user.ConfirmMfaEnrollment(hashes, now);
        if (confirm.IsFailure)
            return confirm.Error;

        await db.SaveChangesAsync(cancellationToken);
        return new MfaRecoveryCodesDto(recoveryCodes);
    }
}

// --- Admin reset ----------------------------------------------------------

public sealed record ResetUserMfaCommand(Guid Id) : ICommand;

public sealed class ResetUserMfaCommandHandler(IIdentityDbContext db, TimeProvider timeProvider)
    : ICommandHandler<ResetUserMfaCommand>
{
    public async Task<Result> Handle(ResetUserMfaCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);
        if (user is null)
            return Error.NotFound("User not found.", "Identity.User.NotFound");

        var now = timeProvider.GetUtcNow();
        user.ResetMfa(now);

        // Force re-authentication everywhere; the user must re-enroll MFA.
        var sessions = await db.Sessions.Where(s => s.UserId == user.Id && s.RevokedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var session in sessions)
            session.Revoke(now);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
