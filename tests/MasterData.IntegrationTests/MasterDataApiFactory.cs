using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Infrastructure.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Testcontainers.MsSql;

namespace MasterData.IntegrationTests;

/// <summary>
/// Boots the real API host against a throwaway SQL Server container. Identity + MasterData migrations
/// and seeding (including the ISO country baseline) run on startup, so tests exercise the genuine stack.
/// </summary>
public sealed class MasterDataApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public const string AdminEmail = "admin@nags.sa";
    public const string AdminPassword = "Admin#12345";
    public const string IdentityBase = "/api/v1/identity";
    public const string Base = "/api/v1/masterdata";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        var connectionString = _sql.GetConnectionString().Replace("Database=master", "Database=MasterDataTests");

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

    public async Task<HttpClient> CreateAuthenticatedAdminClientAsync()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync($"{IdentityBase}/auth/login",
            new { email = AdminEmail, password = AdminPassword });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    /// <summary>
    /// Synchronously drains every module outbox several times so a cross-module flow
    /// (MasterData → Identity → MasterData) completes deterministically without waiting for the
    /// background Quartz job. Inbox dedupe makes repeated draining safe.
    /// </summary>
    public async Task DrainOutboxesAsync(int rounds = 4)
    {
        for (var i = 0; i < rounds; i++)
        {
            await using var scope = Services.CreateAsyncScope();
            foreach (var processor in scope.ServiceProvider.GetServices<IOutboxProcessor>())
                await processor.ProcessAsync();
        }
    }

    public async Task InitializeAsync() => await _sql.StartAsync();

    public new async Task DisposeAsync()
    {
        await _sql.DisposeAsync();
        await base.DisposeAsync();
    }

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);
}
