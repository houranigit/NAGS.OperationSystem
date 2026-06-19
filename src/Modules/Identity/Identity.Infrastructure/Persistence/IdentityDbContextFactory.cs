using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Identity.Infrastructure.Persistence;

/// <summary>
/// Enables <c>dotnet ef migrations</c> without booting the host. The connection string here is
/// only used for design-time model building, not at runtime.
/// </summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer("Server=localhost;Database=OperationsSystem;Trusted_Connection=False;TrustServerCertificate=True;",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema))
            .Options;

        return new IdentityDbContext(options);
    }
}
