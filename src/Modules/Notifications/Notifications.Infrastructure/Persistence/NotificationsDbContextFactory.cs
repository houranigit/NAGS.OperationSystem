using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Notifications.Infrastructure.Persistence;

public sealed class NotificationsDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Notifications")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=OperationsSystem;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True;Encrypt=False";
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", NotificationsDbContext.Schema))
            .Options;
        return new NotificationsDbContext(options);
    }
}
