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

public sealed record LoginCommand(string Email, string Password, string? IpAddress, string? UserAgent) : ICommand<LoginResultDto>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class LoginCommandHandler(
    IIdentityDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IMfaSecretProtector secretProtector,
    TimeProvider timeProvider,
    IOptions<IdentityModuleOptions> options)
    : ICommandHandler<LoginCommand, LoginResultDto>
{
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("Invalid email or password.", "Identity.Auth.InvalidCredentials");

    private readonly IdentityModuleOptions _options = options.Value;

    public async Task<Result<LoginResultDto>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure)
            return InvalidCredentials;

        var emailValue = emailResult.Value.Value;
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Email.Value == emailValue && !u.LoginEmailReleased, cancellationToken);

        var now = timeProvider.GetUtcNow();

        // Only an active account can sign in. Invited, Suspended, and Deactivated accounts all return
        // the same generic failure so the endpoint does not reveal an account's lifecycle state.
        if (user is null || user.Status != UserStatus.Active || user.PasswordHash is null)
            return InvalidCredentials;

        if (user.IsLockedOut(now))
            return Error.Unauthorized("Account is locked. Please try again later.", "Identity.Auth.LockedOut");

        if (!passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            var locked = user.RecordFailedSignIn(_options.MaxFailedSignInAttempts, TimeSpan.FromMinutes(_options.LockoutMinutes), now);
            if (locked)
                await AuthSessionRevocation.RevokeActiveSessionsAsync(db, user.Id, now, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            return InvalidCredentials;
        }

        // When the account has MFA enrolled, stop here and require the second step. No session is
        // created and the failed-attempt counter is reset only once the full sign-in completes.
        if (user.MfaEnabled)
        {
            if (string.IsNullOrWhiteSpace(user.MfaSecret)
                || !secretProtector.TryUnprotect(user.MfaSecret, out _))
            {
                user.ResetMfa(now);
            }
            else
            {
                await db.SaveChangesAsync(cancellationToken);
                var mfaToken = tokenService.CreateMfaChallengeToken(user);
                return new LoginResultDto(MfaRequired: true, MfaToken: mfaToken, Tokens: null);
            }
        }

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == user.RoleId, cancellationToken);
        var permissions = EffectiveUserPermissions.For(user, role);

        user.RecordSuccessfulSignIn(now);

        var refresh = tokenService.CreateRefreshToken();

        var sessionResult = Domain.Sessions.UserSession.Issue(
            user.Id, refresh.Hash, refresh.ExpiresAtUtc, now, request.IpAddress, request.UserAgent);
        if (sessionResult.IsFailure)
            return sessionResult.Error;

        db.Sessions.Add(sessionResult.Value);

        // The access token is bound to this session so revoking it invalidates the token too.
        var access = tokenService.CreateAccessToken(user, permissions, sessionResult.Value.Id);
        await db.SaveChangesAsync(cancellationToken);

        return new LoginResultDto(MfaRequired: false, MfaToken: null,
            Tokens: new AuthTokensDto(access.Value, access.ExpiresAtUtc, refresh.Value, refresh.ExpiresAtUtc));
    }
}
