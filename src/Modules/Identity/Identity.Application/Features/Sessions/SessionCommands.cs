using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Sessions;

// --- Admin: revoke a single session by id ---------------------------------

public sealed record RevokeSessionCommand(Guid SessionId) : ICommand;

public sealed class RevokeSessionCommandHandler(
    IIdentityDbContext db,
    IUserContext userContext,
    IPermissionRegistry permissions,
    TimeProvider timeProvider)
    : ICommandHandler<RevokeSessionCommand>
{
    public async Task<Result> Handle(RevokeSessionCommand request, CancellationToken cancellationToken)
    {
        // Resolve the immutable family id before opening a SERIALIZABLE access transaction. Once
        // inside, acquire the family applock before reading session rows; this is the same lock
        // order used by refresh and avoids an S-row-lock ↔ family-applock inversion.
        var familyId = await db.Sessions.AsNoTracking()
            .Where(session => session.Id == request.SessionId)
            .Select(session => (Guid?)session.FamilyId)
            .FirstOrDefaultAsync(cancellationToken);
        if (familyId is null)
            return Error.NotFound("Session not found.", "Identity.Session.NotFound");

        try
        {
            await using var transaction =
                await db.BeginAccessManagementTransactionAsync(cancellationToken);

            var authorizationTime = timeProvider.GetUtcNow();
            var liveActorRole = await RoleAssignmentAuthorization.GetLiveActorRoleAsync(
                db,
                userContext,
                permissions,
                IdentityPermissions.Sessions.Revoke,
                authorizationTime,
                cancellationToken);
            if (liveActorRole.IsFailure)
                return liveActorRole.Error;

            await db.AcquireSessionFamilyLockAsync(
                familyId.Value,
                cancellationToken);

            var session = await db.Sessions.FirstOrDefaultAsync(
                candidate => candidate.Id == request.SessionId,
                cancellationToken);
            if (session is null)
                return Error.NotFound("Session not found.", "Identity.Session.NotFound");

            var mutationTime = timeProvider.GetUtcNow();
            var familySessions = await db.Sessions
                .Where(candidate =>
                    candidate.UserId == session.UserId &&
                    candidate.FamilyId == session.FamilyId &&
                    candidate.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var familySession in familySessions)
                familySession.Revoke(mutationTime);

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

// --- Admin: revoke all active sessions for a user -------------------------

public sealed record RevokeUserSessionsCommand(Guid UserId) : ICommand;

public sealed class RevokeUserSessionsCommandHandler(
    IIdentityDbContext db,
    IUserContext userContext,
    IPermissionRegistry permissions,
    TimeProvider timeProvider)
    : ICommandHandler<RevokeUserSessionsCommand>
{
    public async Task<Result> Handle(RevokeUserSessionsCommand request, CancellationToken cancellationToken)
    {
        var familyIds = await db.Sessions.AsNoTracking()
            .Where(session =>
                session.UserId == request.UserId &&
                session.RevokedAtUtc == null)
            .Select(session => session.FamilyId)
            .Distinct()
            .OrderBy(familyId => familyId)
            .ToListAsync(cancellationToken);

        try
        {
            await using var transaction =
                await db.BeginAccessManagementTransactionAsync(cancellationToken);

            var authorizationTime = timeProvider.GetUtcNow();
            var liveActorRole = await RoleAssignmentAuthorization.GetLiveActorRoleAsync(
                db,
                userContext,
                permissions,
                IdentityPermissions.Sessions.Revoke,
                authorizationTime,
                cancellationToken);
            if (liveActorRole.IsFailure)
                return liveActorRole.Error;

            foreach (var familyId in familyIds)
            {
                await db.AcquireSessionFamilyLockAsync(
                    familyId,
                    cancellationToken);
            }

            var user = await db.Users.FirstOrDefaultAsync(
                user => user.Id == request.UserId,
                cancellationToken);
            if (user is null)
                return Error.NotFound("User not found.", "Identity.User.NotFound");

            var mutationTime = timeProvider.GetUtcNow();
            // Revoking every session is an authorization-generation change. Rotating the stamp
            // additionally invalidates a login/session inserted outside the known family set.
            user.RotateSecurityStamp(mutationTime);
            var sessions = await db.Sessions
                .Where(session =>
                    session.UserId == request.UserId &&
                    session.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);

            foreach (var session in sessions)
                session.Revoke(mutationTime);

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

// --- Self: revoke one of my own sessions ----------------------------------

public sealed record RevokeMySessionCommand(Guid SessionId) : ICommand;

public sealed class RevokeMySessionCommandHandler(
    IIdentityDbContext db,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
    : ICommandHandler<RevokeMySessionCommand>
{
    public async Task<Result> Handle(RevokeMySessionCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Error.Unauthorized();

        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken);
        if (session is null || session.UserId != userId)
            return Error.NotFound("Session not found.", "Identity.Session.NotFound");

        await using var transaction =
            await db.BeginSessionFamilyTransactionAsync(
                session.FamilyId,
                cancellationToken);
        await db.ReloadAsync(session, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var familySessions = await db.Sessions
            .Where(candidate =>
                candidate.UserId == userId &&
                candidate.FamilyId == session.FamilyId &&
                candidate.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var familySession in familySessions)
            familySession.Revoke(now);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}

// --- Self: revoke all my other sessions ("sign out other devices") --------

public sealed record RevokeMyOtherSessionsCommand(string? CurrentRefreshToken) : ICommand;

public sealed class RevokeMyOtherSessionsCommandHandler(
    IIdentityDbContext db,
    ICurrentUser currentUser,
    ITokenService tokenService,
    TimeProvider timeProvider)
    : ICommandHandler<RevokeMyOtherSessionsCommand>
{
    public async Task<Result> Handle(RevokeMyOtherSessionsCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Error.Unauthorized();

        Guid? currentFamilyId = null;
        if (!string.IsNullOrWhiteSpace(request.CurrentRefreshToken))
        {
            var currentHash = tokenService.HashRefreshToken(request.CurrentRefreshToken);
            currentFamilyId = await db.Sessions
                .Where(session =>
                    session.UserId == userId &&
                    session.RefreshTokenHash == currentHash)
                .Select(session => (Guid?)session.FamilyId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var familyIds = await db.Sessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null)
            .Where(session =>
                currentFamilyId == null ||
                session.FamilyId != currentFamilyId.Value)
            .Select(session => session.FamilyId)
            .Distinct()
            .OrderBy(familyId => familyId)
            .ToListAsync(cancellationToken);

        await using var transaction =
            await db.BeginAccessManagementTransactionAsync(cancellationToken);
        foreach (var familyId in familyIds)
            await db.AcquireSessionFamilyLockAsync(familyId, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var sessions = await db.Sessions
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions.Where(
                     candidate =>
                         currentFamilyId is null ||
                         candidate.FamilyId != currentFamilyId.Value))
        {
            session.Revoke(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}
