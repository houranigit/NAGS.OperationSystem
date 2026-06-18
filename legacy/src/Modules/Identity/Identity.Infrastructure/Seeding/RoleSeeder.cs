using Identity.Application.Authorization;
using Identity.Domain.Aggregates.Role;
using Identity.Domain.Authorization;
using Identity.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Identity.Infrastructure.Seeding;

public static class RoleSeeder
{
    public const string SystemAdminRoleName = "System Admin";
    public const string DispatcherRoleName = "Dispatcher";

    private static readonly IReadOnlySet<string> DispatcherPermissions = new HashSet<string>
    {
        Permissions.Scheduler.Read,
        Permissions.Scheduler.ReadLookups,
        Permissions.Flights.Read,
        Permissions.Flights.Create,
        Permissions.Flights.Update
    };

    public static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<IdentityDbContext>>();
        var roleRepo = scope.ServiceProvider.GetRequiredService<IRoleRepository>();

        await EnsureRoleExistsAsync(
            roleRepo,
            SystemAdminRoleName,
            "Full access to manage the operations portal.",
            logger);

        await EnsureRoleExistsAsync(
            roleRepo,
            DispatcherRoleName,
            "Access to the flight scheduler for creating and updating scheduled flights.",
            logger);

        await db.SaveChangesAsync();

        await EnsureRolePermissionsAsync(
            roleRepo,
            SystemAdminRoleName,
            PermissionCatalog.All.Select(p => p.Code),
            logger);

        await EnsureRolePermissionsAsync(
            roleRepo,
            DispatcherRoleName,
            DispatcherPermissions,
            logger);

        await db.SaveChangesAsync();
    }

    private static async Task EnsureRoleExistsAsync(
        IRoleRepository roleRepo,
        string name,
        string description,
        ILogger logger)
    {
        if (await roleRepo.GetByNameAsync(name) is not null)
            return;

        var created = Role.Create(name, description, isSystemRole: true);
        if (created.IsFailure)
        {
            logger.LogError("Failed to seed role {RoleName}: {Error}", name, created.Error.Description);
            return;
        }

        roleRepo.Add(created.Value);
    }

    private static async Task EnsureRolePermissionsAsync(
        IRoleRepository roleRepo,
        string name,
        IEnumerable<string> permissions,
        ILogger logger)
    {
        var role = await roleRepo.GetByNameAsync(name);
        if (role is null)
            return;

        foreach (var permission in permissions.Distinct(StringComparer.Ordinal))
        {
            if (role.Permissions.Any(p => p.Permission == permission))
                continue;

            var added = role.AddPermission(permission);
            if (added.IsFailure)
                logger.LogError("Failed to add permission {Permission} to role {RoleName}: {Error}", permission, name, added.Error.Description);
        }

        roleRepo.Update(role);
    }
}
