using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Operations.Infrastructure.Persistence;

/// <summary>Enables <c>dotnet ef migrations</c> for Operations without booting the host.</summary>
public sealed class OperationsDbContextFactory : IDesignTimeDbContextFactory<OperationsDbContext>
{
    public OperationsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OperationsDbContext>()
            .UseSqlServer(GetConnectionString(),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", OperationsDbContext.Schema))
            .Options;

        return new OperationsDbContext(options);
    }

    private static string GetConnectionString() =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Operations")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? "Server=localhost,1433;Database=OperationsSystem;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True;Encrypt=False";
}
