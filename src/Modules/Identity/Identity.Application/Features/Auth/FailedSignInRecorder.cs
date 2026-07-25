using Identity.Application.Abstractions;
using Identity.Application.Features.Users;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Auth;

/// <summary>
/// Records failed sign-ins inside the same serialized boundary used by administrative access
/// changes. This prevents a final lockout and a concurrent demotion from jointly removing every
/// sign-in-capable holder of the protected System Administrator role.
/// </summary>
internal static class FailedSignInRecorder
{
    public static async Task RecordAsync(
        IIdentityDbContext db,
        User user,
        Func<User, bool> remainsAValidFailure,
        int maxFailedAttempts,
        TimeSpan lockoutDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction =
                await db.BeginAccessManagementTransactionAsync(cancellationToken);

            // Authentication reads happen before the lock to avoid serializing successful logins.
            // Refresh the tracked aggregate now that the access-management state is stable.
            await db.ReloadAsync(user, cancellationToken);
            if (!remainsAValidFailure(user))
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var reachesLockoutThreshold =
                user.AccessFailedCount + 1 >= Math.Max(1, maxFailedAttempts);
            var allowLockout =
                !reachesLockoutThreshold ||
                !await UserLifecycleGuards.IsLastSignInCapableAdminAsync(
                    db,
                    user,
                    now,
                    cancellationToken);

            var locked = user.RecordFailedSignIn(
                maxFailedAttempts,
                lockoutDuration,
                now,
                allowLockout);
            if (locked)
            {
                await AuthSessionRevocation.RevokeActiveSessionsAsync(
                    db,
                    user.Id,
                    now,
                    cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another user/security update won. The authentication request still fails, but it
            // must not overwrite or lock based on its stale pre-lock snapshot.
        }
    }
}
