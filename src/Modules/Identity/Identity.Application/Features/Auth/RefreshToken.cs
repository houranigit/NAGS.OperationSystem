using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
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
    IPermissionRegistry permissionRegistry,
    TimeProvider timeProvider)
    : ICommandHandler<RefreshTokenCommand, AuthTokensDto>
{
    public const string ConsumedTokenErrorCode = "Identity.Auth.RefreshTokenConsumed";

    private static readonly Error Invalid =
        Error.Unauthorized("Invalid or expired refresh token.", "Identity.Auth.InvalidRefreshToken");
    private static readonly Error Consumed =
        Error.Unauthorized("Invalid or expired refresh token.", ConsumedTokenErrorCode);

    public async Task<Result<AuthTokensDto>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var now = timeProvider.GetUtcNow();
            var hash = tokenService.HashRefreshToken(request.RefreshToken);

            var session = await db.Sessions.FirstOrDefaultAsync(
                candidate => candidate.RefreshTokenHash == hash,
                cancellationToken);
            if (session is null)
                return Invalid;
            if (session.RevokedAtUtc is not null)
                return Consumed;
            if (session.ExpiresAtUtc <= now)
                return Invalid;

            await using var transaction =
                await db.BeginSessionFamilyTransactionAsync(
                    session.FamilyId,
                    cancellationToken);

            // The token lookup precedes the family lock. Refresh the row so a rotation or logout
            // that committed while this request waited is observed before any token is minted.
            await db.ReloadAsync(session, cancellationToken);
            now = timeProvider.GetUtcNow();
            if (session.RevokedAtUtc is not null)
                return Consumed;
            if (session.ExpiresAtUtc <= now)
                return Invalid;

            var user = await db.Users.FirstOrDefaultAsync(
                candidate => candidate.Id == session.UserId,
                cancellationToken);
            if (user is null
                || user.Status != UserStatus.Active
                || user.IsLockedOut(now)
                || session.SecurityStamp != user.SecurityStamp)
            {
                session.Revoke(now);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Invalid;
            }

            // Rotate the presented single-use token. UserSession.RowVersion makes consumption
            // atomic: only one concurrent refresh/revocation can update this predecessor, and EF's
            // SaveChanges transaction rolls the successor insert back when the update loses.
            session.Revoke(now);

            var role = await db.Roles.FirstOrDefaultAsync(
                candidate => candidate.Id == user.RoleId,
                cancellationToken);
            var permissions = EffectiveUserPermissions.For(user, role, permissionRegistry);
            if (permissions.IsFailure)
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Invalid;
            }

            var refresh = tokenService.CreateRefreshToken();
            var newSession = session.ContinueWith(
                refresh.Hash,
                refresh.ExpiresAtUtc,
                now,
                request.IpAddress,
                request.UserAgent);
            if (newSession.IsFailure)
                return newSession.Error;

            db.Sessions.Add(newSession.Value);

            var access = tokenService.CreateAccessToken(
                user,
                permissions.Value,
                newSession.Value.Id);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new AuthTokensDto(
                access.Value,
                access.ExpiresAtUtc,
                refresh.Value,
                refresh.ExpiresAtUtc);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent refresh or revocation consumed the same predecessor. Never expose a
            // second token pair and do not turn token reuse into an internal-server error.
            return Consumed;
        }
    }
}
