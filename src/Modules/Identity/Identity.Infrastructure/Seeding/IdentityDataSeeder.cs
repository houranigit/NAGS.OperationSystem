using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Seeding;

/// <summary>
/// Seeds the System Administrator role (all permissions) and the bootstrap admin account.
/// Idempotent: safe to run on every startup.
/// </summary>
public sealed class IdentityDataSeeder(
    IdentityDbContext db,
    IPasswordHasher passwordHasher,
    IPermissionRegistry permissionRegistry,
    ITokenService tokenService,
    IInvitationNotifier invitationNotifier,
    TimeProvider timeProvider,
    IOptions<IdentityModuleOptions> options,
    ILogger<IdentityDataSeeder> logger)
{
    private readonly IdentityModuleOptions _options = options.Value;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var normalizedAdminRole = IdentitySeedIds.SystemAdminRoleName.ToUpperInvariant();

        var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.NormalizedName == normalizedAdminRole, cancellationToken);
        if (adminRole is null)
        {
            var roleResult = Role.Create(
                IdentitySeedIds.SystemAdminRoleName,
                "Full system access. Seeded and protected.",
                AllKnownPermissions(),
                UserType.SystemAdministrator,
                now,
                isSystem: true);

            if (roleResult.IsFailure)
            {
                logger.LogError("Failed to seed System Administrator role: {Error}", roleResult.Error.Description);
                return;
            }

            adminRole = roleResult.Value;
            db.Roles.Add(adminRole);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded System Administrator role {RoleId}.", adminRole.Id);
        }
        else
        {
            // Keep the system admin role's permissions in sync as the catalog grows.
            adminRole.SetPermissions(AllKnownPermissions(), now);
            await db.SaveChangesAsync(cancellationToken);
        }

        await SeedBootstrapAdminAsync(adminRole.Id, now, cancellationToken);

        await SeedDemoDataAsync(cancellationToken);
    }

    private async Task SeedBootstrapAdminAsync(Guid adminRoleId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(_options.Admin.Email);
        if (emailResult.IsFailure)
        {
            logger.LogError("Cannot seed admin user: invalid configured email '{Email}'.", _options.Admin.Email);
            return;
        }

        var email = emailResult.Value;
        var existing = await db.Users.FirstOrDefaultAsync(
            u => u.Email.Value == email.Value && !u.LoginEmailReleased,
            cancellationToken);

        if (existing is not null)
        {
            if (existing.UserType != UserType.SystemAdministrator)
            {
                logger.LogWarning(
                    "Configured bootstrap admin email {Email} belongs to a {UserType} account; seeding will not elevate it.",
                    email.Value, existing.UserType);
                return;
            }

            var roleReassigned = false;
            SecureToken? invitation = null;

            if (existing.RoleId != adminRoleId)
            {
                var assignResult = existing.AssignRole(adminRoleId, now);
                if (assignResult.IsFailure)
                {
                    logger.LogError("Failed to reassign bootstrap admin {Email}: {Error}", email.Value, assignResult.Error.Description);
                    return;
                }

                roleReassigned = true;
            }

            if (ShouldRequeueBootstrapInvitation(existing, now))
            {
                invitation = tokenService.CreateSecureToken();
                var resendResult = existing.ResendInvitation(
                    invitation.Hash,
                    now.AddHours(_options.InvitationExpiryHours),
                    now);

                if (resendResult.IsFailure)
                {
                    logger.LogError("Failed to refresh bootstrap admin invitation for {Email}: {Error}", email.Value, resendResult.Error.Description);
                    return;
                }
            }

            if (roleReassigned || invitation is not null)
            {
                await db.SaveChangesAsync(cancellationToken);

                if (roleReassigned)
                    logger.LogInformation("Reassigned bootstrap admin {Email} to the protected System Administrator role.", email.Value);

                if (invitation is not null)
                {
                    await invitationNotifier.SendInvitationAsync(
                        email.Value, existing.DisplayName, existing.Id, invitation.Value, cancellationToken);
                    logger.LogInformation("Refreshed expired bootstrap admin invitation for {Email}. Activate via the emailed link.", email.Value);
                }
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(_options.Admin.Password))
        {
            // Production posture: no default password. Bootstrap the administrator as an invitation;
            // the activation token is delivered by email (never logged or defaulted).
            var token = tokenService.CreateSecureToken();
            var inviteResult = User.Invite(
                email,
                _options.Admin.DisplayName,
                adminRoleId,
                token.Hash,
                now.AddHours(_options.InvitationExpiryHours),
                now);

            if (inviteResult.IsFailure)
            {
                logger.LogError("Failed to seed bootstrap admin invitation: {Error}", inviteResult.Error.Description);
                return;
            }

            db.Users.Add(inviteResult.Value);
            await db.SaveChangesAsync(cancellationToken);
            await invitationNotifier.SendInvitationAsync(
                email.Value, _options.Admin.DisplayName, inviteResult.Value.Id, token.Value, cancellationToken);
            logger.LogInformation("Seeded bootstrap admin invitation for {Email}. Activate via the emailed link.", email.Value);
        }
        else
        {
            // Explicit password configured (development/test): create an already-active admin.
            var userResult = User.CreateActive(
                email,
                _options.Admin.DisplayName,
                adminRoleId,
                passwordHasher.Hash(_options.Admin.Password),
                now);

            if (userResult.IsFailure)
            {
                logger.LogError("Failed to seed admin user: {Error}", userResult.Error.Description);
                return;
            }

            db.Users.Add(userResult.Value);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded bootstrap admin user {Email}.", email.Value);
        }
    }

    private async Task SeedDemoDataAsync(CancellationToken cancellationToken)
    {
        var demo = _options.DemoData;
        if (!demo.Enabled || (demo.RoleCount <= 0 && demo.UserCount <= 0))
            return;

        var now = timeProvider.GetUtcNow();
        var rolesCreated = 0;

        if (demo.RoleCount > 0)
        {
            var existingDemoRoleNames = await db.Roles
                .Where(r => r.Name.StartsWith("Demo Role "))
                .Select(r => r.NormalizedName)
                .ToHashSetAsync(cancellationToken);

            for (var i = 1; i <= demo.RoleCount; i++)
            {
                var roleName = $"Demo Role {i:000}";
                if (existingDemoRoleNames.Contains(roleName.ToUpperInvariant()))
                    continue;

                var roleResult = Role.Create(
                    roleName,
                    $"Pagination demo role {i}",
                    [IdentityPermissions.Roles.View],
                    UserType.SystemAdministrator,
                    now);

                if (roleResult.IsFailure)
                {
                    logger.LogWarning("Skipped demo role {Index}: {Error}", i, roleResult.Error.Description);
                    continue;
                }

                db.Roles.Add(roleResult.Value);
                rolesCreated++;
            }
        }

        Guid demoUserRoleId;
        const string demoUserRoleName = "Demo User Role";
        var demoUserRole = await db.Roles.FirstOrDefaultAsync(
            r => r.NormalizedName == demoUserRoleName.ToUpperInvariant(),
            cancellationToken);

        if (demoUserRole is null)
        {
            var demoUserRoleResult = Role.Create(
                demoUserRoleName,
                "Shared role for pagination demo users.",
                [IdentityPermissions.Users.View],
                UserType.SystemAdministrator,
                now);

            if (demoUserRoleResult.IsFailure)
            {
                logger.LogWarning("Failed to seed demo user role: {Error}", demoUserRoleResult.Error.Description);
                return;
            }

            demoUserRole = demoUserRoleResult.Value;
            db.Roles.Add(demoUserRole);
            rolesCreated++;
        }

        demoUserRoleId = demoUserRole.Id;

        if (rolesCreated > 0)
            await db.SaveChangesAsync(cancellationToken);

        if (demo.UserCount <= 0)
            return;

        var existingDemoUserEmails = await db.Users
            .Where(u => u.Email.Value.StartsWith("demo-user-"))
            .Select(u => u.Email.Value)
            .ToHashSetAsync(StringComparer.OrdinalIgnoreCase);

        var usersCreated = 0;
        for (var i = 1; i <= demo.UserCount; i++)
        {
            var email = $"demo-user-{i:000}@nags.sa";
            if (existingDemoUserEmails.Contains(email))
                continue;

            var emailResult = Email.Create(email);
            if (emailResult.IsFailure)
                continue;

            var userResult = User.CreateActive(
                emailResult.Value,
                $"Demo User {i:000}",
                demoUserRoleId,
                passwordHasher.Hash("Demo#12345"),
                now);

            if (userResult.IsFailure)
            {
                logger.LogWarning("Skipped demo user {Index}: {Error}", i, userResult.Error.Description);
                continue;
            }

            db.Users.Add(userResult.Value);
            usersCreated++;
        }

        if (usersCreated > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Seeded pagination demo data: {RoleCount} roles and {UserCount} users.",
                rolesCreated,
                usersCreated);
        }
    }

    private List<string> AllKnownPermissions() => permissionRegistry.All.Select(p => p.Code).ToList();

    private static bool ShouldRequeueBootstrapInvitation(User user, DateTimeOffset now) =>
        user.Status == UserStatus.Invited
        && (user.InvitationToken is null
            || user.InvitationExpiresAtUtc is null
            || user.InvitationExpiresAtUtc <= now);
}
