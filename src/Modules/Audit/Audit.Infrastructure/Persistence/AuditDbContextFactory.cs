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
            .UseSqlServer("Server=localhost;Database=OperationsSystem;Trusted_Connection=False;TrustServerCertificate=True;",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AuditDbContext.Schema))
            .Options;

        return new AuditDbContext(options);
    }
}
