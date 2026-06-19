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

        var anyUsers = await db.Users.AnyAsync(cancellationToken);
        if (anyUsers)
            return;

        var emailResult = Email.Create(_options.Admin.Email);
        if (emailResult.IsFailure)
        {
            logger.LogError("Cannot seed admin user: invalid configured email '{Email}'.", _options.Admin.Email);
            return;
        }

        var password = string.IsNullOrWhiteSpace(_options.Admin.Password) ? "ChangeMe!123" : _options.Admin.Password;
        if (string.IsNullOrWhiteSpace(_options.Admin.Password))
            logger.LogWarning("No Identity:Admin:Password configured. Seeding admin with a default dev password. CHANGE IT.");

        var userResult = User.CreateActive(
            emailResult.Value,
            _options.Admin.DisplayName,
            adminRole.Id,
            passwordHasher.Hash(password),
            now);

        if (userResult.IsFailure)
        {
            logger.LogError("Failed to seed admin user: {Error}", userResult.Error.Description);
            return;
        }

        db.Users.Add(userResult.Value);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded bootstrap admin user {Email}.", emailResult.Value.Value);
    }

    private static List<string> AllKnownPermissions() => IdentityPermissions.All.ToList();
}
