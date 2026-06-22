using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MasterData.Infrastructure.Persistence;

/// <summary>Enables <c>dotnet ef migrations</c> for MasterData without booting the host.</summary>
public sealed class MasterDataDbContextFactory : IDesignTimeDbContextFactory<MasterDataDbContext>
{
    public MasterDataDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MasterDataDbContext>()
            .UseSqlServer("Server=localhost;Database=OperationsSystem;Trusted_Connection=False;TrustServerCertificate=True;",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", MasterDataDbContext.Schema))
            .Options;

        return new MasterDataDbContext(options);
    }
}
