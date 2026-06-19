using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Infrastructure.Notifications;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Seeding;
using Identity.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Identity.Infrastructure;

public static class IdentityInfrastructureExtensions
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdentityModuleOptions>()
            .Bind(configuration.GetSection(IdentityModuleOptions.SectionName))
            .ValidateOnStart();

        var connectionString = configuration.GetConnectionString("Identity")
            ?? configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("No 'Identity' or 'Default' connection string configured.");

        services.AddDbContext<IdentityDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema)));

        services.AddScoped<IIdentityDbContext>(sp => sp.GetRequiredService<IdentityDbContext>());

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();

        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IInvitationNotifier, LoggingInvitationNotifier>();

        services.AddScoped<IdentityDataSeeder>();

        return services;
    }

    public static async Task MigrateAndSeedIdentityAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        var seeder = scope.ServiceProvider.GetRequiredService<IdentityDataSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
