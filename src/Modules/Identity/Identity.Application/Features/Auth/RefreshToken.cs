using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Application.Contracts;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Auth;

public sealed record RefreshTokenCommand(string RefreshToken, string? IpAddress, string? UserAgent) : ICommand<AuthTokensDto>;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}

public sealed class RefreshTokenCommandHandler(
    IIdentityDbContext db,
    ITokenService tokenService,
    TimeProvider timeProvider)
    : ICommandHandler<RefreshTokenCommand, AuthTokensDto>
{
    private static readonly Error Invalid =
        Error.Unauthorized("Invalid or expired refresh token.", "Identity.Auth.InvalidRefreshToken");

    public async Task<Result<AuthTokensDto>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var hash = tokenService.HashRefreshToken(request.RefreshToken);

        var session = await db.Sessions.FirstOrDefaultAsync(s => s.RefreshTokenHash == hash, cancellationToken);
        if (session is null || !session.IsActive(now))
            return Invalid;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == session.UserId, cancellationToken);
        if (user is null || user.Status != UserStatus.Active || user.IsLockedOut(now))
        {
            session.Revoke(now);
            await db.SaveChangesAsync(cancellationToken);
            return Invalid;
        }

        // Rotate: revoke the presented session and issue a fresh one.
        session.Revoke(now);

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == user.RoleId, cancellationToken);
        var permissions = role?.Permissions.ToList() ?? [];

        var refresh = tokenService.CreateRefreshToken();

        var newSession = UserSessionFactory(user.Id, refresh, now, request);
        if (newSession.IsFailure)
            return newSession.Error;

        db.Sessions.Add(newSession.Value);

        var access = tokenService.CreateAccessToken(user, permissions, newSession.Value.Id);
        await db.SaveChangesAsync(cancellationToken);

        return new AuthTokensDto(access.Value, access.ExpiresAtUtc, refresh.Value, refresh.ExpiresAtUtc);
    }

    private static Result<Domain.Sessions.UserSession> UserSessionFactory(
        Guid userId, RefreshToken refresh, DateTimeOffset now, RefreshTokenCommand request) =>
        Domain.Sessions.UserSession.Issue(userId, refresh.Hash, refresh.ExpiresAtUtc, now, request.IpAddress, request.UserAgent);
}
