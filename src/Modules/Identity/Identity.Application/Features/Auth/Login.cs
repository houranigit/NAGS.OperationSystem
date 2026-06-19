using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Application.Contracts;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Identity.Application.Features.Auth;

public sealed record LoginCommand(string Email, string Password, string? IpAddress, string? UserAgent) : ICommand<AuthTokensDto>;

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
    TimeProvider timeProvider,
    IOptions<IdentityModuleOptions> options)
    : ICommandHandler<LoginCommand, AuthTokensDto>
{
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("Invalid email or password.", "Identity.Auth.InvalidCredentials");

    private readonly IdentityModuleOptions _options = options.Value;

    public async Task<Result<AuthTokensDto>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure)
            return InvalidCredentials;

        var emailValue = emailResult.Value.Value;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.Value == emailValue, cancellationToken);

        var now = timeProvider.GetUtcNow();

        if (user is null || user.Status == UserStatus.Deactivated || user.PasswordHash is null)
            return InvalidCredentials;

        if (user.IsLockedOut(now))
            return Error.Unauthorized("Account is locked. Please try again later.", "Identity.Auth.LockedOut");

        if (!passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            user.RecordFailedSignIn(_options.MaxFailedSignInAttempts, TimeSpan.FromMinutes(_options.LockoutMinutes), now);
            await db.SaveChangesAsync(cancellationToken);
            return InvalidCredentials;
        }

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == user.RoleId, cancellationToken);
        var permissions = role?.Permissions.ToList() ?? [];

        user.RecordSuccessfulSignIn(now);

        var access = tokenService.CreateAccessToken(user, permissions);
        var refresh = tokenService.CreateRefreshToken();

        var sessionResult = Domain.Sessions.UserSession.Issue(
            user.Id, refresh.Hash, refresh.ExpiresAtUtc, now, request.IpAddress, request.UserAgent);
        if (sessionResult.IsFailure)
            return sessionResult.Error;

        db.Sessions.Add(sessionResult.Value);
        await db.SaveChangesAsync(cancellationToken);

        return new AuthTokensDto(access.Value, access.ExpiresAtUtc, refresh.Value, refresh.ExpiresAtUtc);
    }
}
