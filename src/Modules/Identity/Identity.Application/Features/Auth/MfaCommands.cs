using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Application.Contracts;
using Identity.Domain.Authorization;
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
    IPermissionRegistry permissionRegistry,
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

        if (!secretProtector.TryUnprotect(user.MfaSecret, out var secret))
        {
            user.ResetMfa(now);
            await db.SaveChangesAsync(cancellationToken);
            return Invalid;
        }

        var verified = mfaService.VerifyCode(secret, request.Code, now)
            || user.ConsumeRecoveryCode(tokenService.HashToken(request.Code.Trim()), now).IsSuccess;

        if (!verified)
        {
            await FailedSignInRecorder.RecordAsync(
                db,
                user,
                candidate =>
                    candidate.Status == UserStatus.Active &&
                    candidate.SecurityStamp == challenge.SecurityStamp &&
                    !candidate.IsLockedOut(now) &&
                    candidate.MfaEnabled &&
                    candidate.MfaSecret is { } protectedSecret &&
                    secretProtector.TryUnprotect(protectedSecret, out _),
                _options.MaxFailedSignInAttempts,
                TimeSpan.FromMinutes(_options.LockoutMinutes),
                now,
                cancellationToken);
            return Invalid;
        }

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == user.RoleId, cancellationToken);
        var permissions = EffectiveUserPermissions.For(user, role, permissionRegistry);
        if (permissions.IsFailure)
            return Invalid;

        user.RecordSuccessfulSignIn(now);

        var refresh = tokenService.CreateRefreshToken();
        var sessionResult = Domain.Sessions.UserSession.Issue(
            user.Id, user.SecurityStamp, refresh.Hash, refresh.ExpiresAtUtc, now, request.IpAddress, request.UserAgent);
        if (sessionResult.IsFailure)
            return sessionResult.Error;

        db.Sessions.Add(sessionResult.Value);
        var access = tokenService.CreateAccessToken(user, permissions.Value, sessionResult.Value.Id);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Invalid;
        }

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

public sealed record ConfirmMfaCommand(
    string Code,
    string? CurrentRefreshToken = null) : ICommand<MfaRecoveryCodesDto>;

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

        try
        {
            var familyId = await MfaSessionBinding.ResolveCurrentFamilyIdAsync(
                db,
                tokenService,
                userId,
                request.CurrentRefreshToken,
                cancellationToken);

            if (familyId is not { } currentFamilyId)
                return await ApplyAsync(request, userId, null, cancellationToken);

            await using var transaction =
                await db.BeginSessionFamilyTransactionAsync(
                    currentFamilyId,
                    cancellationToken);
            var result = await ApplyAsync(
                request,
                userId,
                currentFamilyId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }
    }

    private async Task<Result<MfaRecoveryCodesDto>> ApplyAsync(
        ConfirmMfaCommand request,
        Guid userId,
        Guid? currentFamilyId,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return Error.NotFound("Account not found.", "Identity.User.NotFound");

        if (user.MfaSecret is null)
            return Error.Conflict("Start MFA enrollment before confirming.", "Identity.User.NoPendingMfa");

        var now = timeProvider.GetUtcNow();
        if (!secretProtector.TryUnprotect(user.MfaSecret, out var secret))
        {
            user.ResetMfa(now);
            await MfaSessionBinding.RebindCurrentAsync(
                db,
                user,
                currentFamilyId,
                now,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Error.Conflict("MFA setup could not be verified. Start setup again.", "Identity.User.InvalidMfaSecret");
        }

        if (!mfaService.VerifyCode(secret, request.Code, now))
            return Error.Validation("That code is not valid. Try again with a fresh code.", "Identity.User.InvalidMfaCode");

        var recoveryCodes = mfaService.GenerateRecoveryCodes(10);
        var hashes = recoveryCodes.Select(tokenService.HashToken).ToList();

        var confirm = user.ConfirmMfaEnrollment(hashes, now);
        if (confirm.IsFailure)
            return confirm.Error;

        await MfaSessionBinding.RebindCurrentAsync(
            db,
            user,
            currentFamilyId,
            now,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new MfaRecoveryCodesDto(recoveryCodes);
    }
}

// --- Admin reset ----------------------------------------------------------

public sealed record ResetUserMfaCommand(Guid Id) : ICommand;

public sealed class ResetUserMfaCommandHandler(
    IIdentityDbContext db,
    IUserContext userContext,
    IPermissionRegistry permissions,
    TimeProvider timeProvider)
    : ICommandHandler<ResetUserMfaCommand>
{
    public async Task<Result> Handle(ResetUserMfaCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction =
                await db.BeginAccessManagementTransactionAsync(cancellationToken);

            var now = timeProvider.GetUtcNow();
            var liveActorRole = await RoleAssignmentAuthorization.GetLiveActorRoleAsync(
                db,
                userContext,
                permissions,
                IdentityPermissions.Users.ResetMfa,
                now,
                cancellationToken);
            if (liveActorRole.IsFailure)
                return liveActorRole.Error;

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken);
            if (user is null)
                return Error.NotFound("User not found.", "Identity.User.NotFound");

            user.ResetMfa(now);

            // Force re-authentication everywhere; the user can re-enroll MFA from account settings.
            var sessions = await db.Sessions
                .Where(s => s.UserId == user.Id && s.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var session in sessions)
                session.Revoke(now);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }
    }
}

// --- Disable (current user) -----------------------------------------------

public sealed record DisableMfaCommand(string? CurrentRefreshToken = null) : ICommand;

public sealed class DisableMfaCommandHandler(
    IIdentityDbContext db,
    ICurrentUser currentUser,
    ITokenService tokenService,
    TimeProvider timeProvider)
    : ICommandHandler<DisableMfaCommand>
{
    public async Task<Result> Handle(DisableMfaCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Error.Unauthorized();

        try
        {
            var familyId = await MfaSessionBinding.ResolveCurrentFamilyIdAsync(
                db,
                tokenService,
                userId,
                request.CurrentRefreshToken,
                cancellationToken);

            if (familyId is not { } currentFamilyId)
                return await ApplyAsync(userId, null, cancellationToken);

            await using var transaction =
                await db.BeginSessionFamilyTransactionAsync(
                    currentFamilyId,
                    cancellationToken);
            var result = await ApplyAsync(
                userId,
                currentFamilyId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }
    }

    private async Task<Result> ApplyAsync(
        Guid userId,
        Guid? currentFamilyId,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return Error.NotFound("Account not found.", "Identity.User.NotFound");

        var now = timeProvider.GetUtcNow();
        user.ResetMfa(now);
        await MfaSessionBinding.RebindCurrentAsync(
            db,
            user,
            currentFamilyId,
            now,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class MfaSessionBinding
{
    public static async Task<Guid?> ResolveCurrentFamilyIdAsync(
        IIdentityDbContext db,
        ITokenService tokenService,
        Guid userId,
        string? currentRefreshToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentRefreshToken))
            return null;

        var hash = tokenService.HashRefreshToken(currentRefreshToken);
        return await db.Sessions.AsNoTracking()
            .Where(session =>
                session.UserId == userId &&
                session.RefreshTokenHash == hash)
            .Select(session => (Guid?)session.FamilyId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public static async Task RebindCurrentAsync(
        IIdentityDbContext db,
        User user,
        Guid? currentFamilyId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var activeSessions = await db.Sessions
            .Where(session =>
                session.UserId == user.Id &&
                session.RevokedAtUtc == null &&
                session.ExpiresAtUtc > now)
            .ToListAsync(cancellationToken);

        var currentSession = currentFamilyId is { } familyId
            ? activeSessions.FirstOrDefault(session => session.FamilyId == familyId)
            : null;

        foreach (var session in activeSessions)
        {
            if (session == currentSession)
                session.RebindSecurityStamp(user.SecurityStamp);
            else
                session.Revoke(now);
        }
    }
}
