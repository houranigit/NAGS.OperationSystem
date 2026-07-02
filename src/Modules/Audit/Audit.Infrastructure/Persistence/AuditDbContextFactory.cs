using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Audit.Infrastructure.Persistence;

/// <summary>
/// Enables <c>dotnet ef migrations</c> without booting the host. The connection string here is only
/// used for design-time model building, not at runtime.
/// </summary>
public sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlServer(GetConnectionString(),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AuditDbContext.Schema))
            .Options;

        return new AuditDbContext(options);
    }

    private static string GetConnectionString() =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Audit")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? "Server=localhost,1433;Database=OperationsSystem;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True;Encrypt=False";
}
