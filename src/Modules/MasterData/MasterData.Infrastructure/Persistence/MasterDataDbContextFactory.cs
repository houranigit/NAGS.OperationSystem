using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MasterData.Infrastructure.Persistence;

/// <summary>Enables <c>dotnet ef migrations</c> for MasterData without booting the host.</summary>
public sealed class MasterDataDbContextFactory : IDesignTimeDbContextFactory<MasterDataDbContext>
{
    public MasterDataDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MasterDataDbContext>()
            .UseSqlServer(GetConnectionString(),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", MasterDataDbContext.Schema))
            .Options;

        return new MasterDataDbContext(options);
    }

    private static string GetConnectionString() =>
        Environment.GetEnvironmentVariable("ConnectionStrings__MasterData")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? "Server=localhost,1433;Database=OperationsSystem;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True;Encrypt=False";
}
