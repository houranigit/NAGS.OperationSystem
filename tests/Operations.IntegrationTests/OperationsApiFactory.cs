using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.MsSql;

namespace Operations.IntegrationTests;

/// <summary>
/// Boots the real API host against a throwaway SQL Server container so Operations endpoints run against
/// the genuine stack (all module migrations run on startup). Requires Docker to be available.
/// </summary>
public sealed class OperationsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public const string Base = "/api/v1/operations";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        var connectionString = _sql.GetConnectionString().Replace("Database=master", "Database=OperationsTests");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
                ["Identity:Jwt:Issuer"] = "operations-system",
                ["Identity:Jwt:Audience"] = "operations-system",
                ["Identity:Jwt:SigningKey"] = "integration-tests-signing-key-must-be-long-enough-1234567890",
                ["Identity:Admin:Email"] = "admin@nags.sa",
                ["Identity:Admin:DisplayName"] = "System Administrator",
                ["Identity:Admin:Password"] = "Admin#12345",
                ["Identity:DemoData:Enabled"] = "false",
                ["Messaging:OutboxDispatchEnabled"] = "false",
                ["Operations:AutoWorkOrder:Enabled"] = "false",
                ["Security:RateLimit:AnonymousAuthPermitLimit"] = "1000000"
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
