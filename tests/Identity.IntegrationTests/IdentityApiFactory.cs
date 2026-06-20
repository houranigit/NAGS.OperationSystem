using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Testcontainers.MsSql;

namespace Identity.IntegrationTests;

/// <summary>
/// Boots the real API host against a throwaway SQL Server container (Testcontainers).
/// Migrations and seeding run on startup, so tests exercise the genuine stack.
/// </summary>
public sealed class IdentityApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public const string AdminEmail = "admin@nags.sa";
    public const string AdminPassword = "Admin#12345";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        var connectionString = _sql.GetConnectionString().Replace("Database=master", "Database=IdentityTests");

        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Identity:DemoData:Enabled", "false");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
                ["Identity:Jwt:Issuer"] = "operations-system",
                ["Identity:Jwt:Audience"] = "operations-system",
                ["Identity:Jwt:SigningKey"] = "integration-tests-signing-key-must-be-long-enough-1234567890",
                ["Identity:Admin:Email"] = AdminEmail,
                ["Identity:Admin:DisplayName"] = "System Administrator",
                ["Identity:Admin:Password"] = AdminPassword,
                ["Identity:DemoData:Enabled"] = "false"
            });
        });
    }

    public async Task InitializeAsync() => await _sql.StartAsync();

    public new async Task DisposeAsync()
    {
        await _sql.DisposeAsync();
        await base.DisposeAsync();
    }
}
