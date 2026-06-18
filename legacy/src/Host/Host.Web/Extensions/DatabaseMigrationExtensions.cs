using Contracts.Infrastructure.Persistence;
using Core.Infrastructure.Persistence;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Notifications.Infrastructure.Persistence;
using Operations.Infrastructure.Persistence;
using Store.Infrastructure.Persistence;

namespace Host.Web.Extensions;

/// <summary>
/// Applies outstanding EF migrations for every module DbContext before seeders run.
/// Order matters: Identity first (baseline), Core next (owns audit.AuditTrails), then
/// Operations, then Notifications.
/// </summary>
public static class DatabaseMigrationExtensions
{
    public static async Task ApplyMigrationsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var identity = sp.GetRequiredService<IdentityDbContext>();
        await identity.Database.MigrateAsync(cancellationToken);

        var core = sp.GetRequiredService<CoreDbContext>();
        await core.Database.MigrateAsync(cancellationToken);

        var store = sp.GetRequiredService<StoreDbContext>();
        await store.Database.MigrateAsync(cancellationToken);

        var contracts = sp.GetRequiredService<ContractsDbContext>();
        await contracts.Database.MigrateAsync(cancellationToken);

        var operations = sp.GetRequiredService<OperationsDbContext>();
        await operations.Database.MigrateAsync(cancellationToken);

        var notifications = sp.GetRequiredService<NotificationsDbContext>();
        await notifications.Database.MigrateAsync(cancellationToken);
    }
}
