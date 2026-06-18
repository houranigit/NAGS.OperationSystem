using Identity.Domain.Aggregates.Role;
using Identity.Domain.Aggregates.User;
using Identity.Domain.ValueObjects;
using Identity.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Identity.Infrastructure.Seeding;

public static class AdminSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<IdentityDbContext>>();
        var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var roleRepo = scope.ServiceProvider.GetRequiredService<IRoleRepository>();

        var email = configuration["Seeding:AdminEmail"] ?? "admin@nags.sa";
        var username = configuration["Seeding:AdminUsername"] ?? "admin";

        var existingByEmail = await userRepo.GetByEmailAsync(email.Trim().ToLowerInvariant());
        if (existingByEmail is not null)
        {
            await EnsureSystemAdminRoleAsync(userRepo, roleRepo, db, existingByEmail);
            return;
        }

        var existingByUsername = await userRepo.GetByEmailOrUsernameAsync(username.Trim().ToLowerInvariant());
        if (existingByUsername is not null)
        {
            await EnsureSystemAdminRoleAsync(userRepo, roleRepo, db, existingByUsername);
            return;
        }

        var emailResult = Email.Create(email);
        var usernameResult = Username.Create(username);

        if (emailResult.IsFailure || usernameResult.IsFailure)
        {
            logger.LogError("Admin seed configuration is invalid — skipping seed.");
            return;
        }

        var userResult = User.CreateSeedAdmin(usernameResult.Value, emailResult.Value);
        if (userResult.IsFailure)
        {
            logger.LogError("Failed to create seed admin: {Error}", userResult.Error.Description);
            return;
        }

        var user = userResult.Value;
        var systemAdminRole = await roleRepo.GetByNameAsync(RoleSeeder.SystemAdminRoleName);
        if (systemAdminRole is not null)
            user.AssignRole(systemAdminRole.Id);

        userRepo.Add(user);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Seed admin created — email: {Email}, activation token: ADMIN001 (never expires).",
            email);
    }

    private static async Task EnsureSystemAdminRoleAsync(
        IUserRepository userRepo,
        IRoleRepository roleRepo,
        IdentityDbContext db,
        User existingUser)
    {
        var systemAdminRole = await roleRepo.GetByNameAsync(RoleSeeder.SystemAdminRoleName);
        if (systemAdminRole is null)
            return;

        var user = await userRepo.GetByIdWithRolesAsync(existingUser.Id);
        if (user is null || user.Roles.Any(r => r.RoleId == systemAdminRole.Id))
            return;

        user.AssignRole(systemAdminRole.Id);
        userRepo.Update(user);
        await db.SaveChangesAsync();
    }
}
