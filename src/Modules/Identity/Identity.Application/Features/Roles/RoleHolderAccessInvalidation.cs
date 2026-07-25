using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Identity.Contracts;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Roles;

/// <summary>
/// Invalidates every live credential whose effective authorization changes with a role grant.
/// Callers must run this inside the same transaction as the role mutation so replacement
/// invitations and the role change either commit together or both roll back.
/// </summary>
internal static class RoleHolderAccessInvalidation
{
    public static async Task<Result> InvalidateAsync(
        IIdentityDbContext db,
        IReadOnlyCollection<Guid> roleIds,
        IInvitationNotifier invitationNotifier,
        ITokenService tokenService,
        int invitationExpiryHours,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0)
            return Result.Success();

        var affectedRoleIds = roleIds.Distinct().ToArray();
        var affectedUsers = await db.Users
            .Where(user =>
                affectedRoleIds.Contains(user.RoleId) &&
                user.Status != UserStatus.Deactivated &&
                !user.LoginEmailReleased)
            .ToListAsync(cancellationToken);

        var replacementInvitations = new List<(User User, SecureToken Token)>();
        foreach (var user in affectedUsers)
        {
            var canceledPendingEmail = user.PendingEmail;
            user.InvalidateAccessCredentials(now);

            if (canceledPendingEmail is not null &&
                user.ExternalReferenceId is { } externalReferenceId &&
                user.UserType.RequiresExternalReference())
            {
                db.Enqueue(new PortalUserEmailChangeFailed
                {
                    ExternalReferenceId = externalReferenceId,
                    UserId = user.Id,
                    UserType = user.UserType,
                    Email = canceledPendingEmail,
                    Reason =
                        "Login email verification was canceled because the account's access changed. Request the email change again."
                });
            }

            if (user.Status != UserStatus.Invited)
                continue;

            var invitation = tokenService.CreateSecureToken();
            var requeue = user.ResendInvitation(
                invitation.Hash,
                now.AddHours(invitationExpiryHours),
                now);
            if (requeue.IsFailure)
                return requeue.Error;

            replacementInvitations.Add((user, invitation));
        }

        if (affectedUsers.Count > 0)
        {
            var affectedUserIds = affectedUsers.Select(user => user.Id).ToArray();
            var sessions = await db.Sessions
                .Where(session =>
                    affectedUserIds.Contains(session.UserId) &&
                    session.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var session in sessions)
                session.Revoke(now);
        }

        foreach (var (user, invitation) in replacementInvitations)
        {
            await invitationNotifier.SendInvitationAsync(
                user.Email.Value,
                user.DisplayName,
                user.Id,
                invitation.Value,
                cancellationToken);
        }

        return Result.Success();
    }
}
