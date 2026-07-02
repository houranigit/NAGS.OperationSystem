using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Infrastructure.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    /// <summary>Captures emails at the delivery boundary so tests can read activation tokens.</summary>
    public CapturingEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(Emails)));

        var connectionString = _sql.GetConnectionString().Replace("Database=master", "Database=IdentityTests");

        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Identity:DemoData:Enabled", "false");
        builder.UseSetting("Messaging:OutboxDispatchEnabled", "false");
        // Tests share a client IP and authenticate frequently; relax the anonymous auth rate limit.
        builder.UseSetting("Security:RateLimit:AnonymousAuthPermitLimit", "1000000");

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
                ["Identity:DemoData:Enabled"] = "false",
                ["Messaging:OutboxDispatchEnabled"] = "false",
                // Tests share a client IP and authenticate frequently; relax the anonymous auth limit.
                ["Security:RateLimit:AnonymousAuthPermitLimit"] = "1000000"
            });
        });
    }

    /// <summary>Drains module outboxes so durable emails are delivered to the capturing sender.</summary>
    public async Task DrainOutboxesAsync(int rounds = 4)
    {
        for (var i = 0; i < rounds; i++)
        {
            await using var scope = Services.CreateAsyncScope();
            foreach (var processor in scope.ServiceProvider.GetServices<IOutboxProcessor>())
                await processor.ProcessAsync();
        }
    }

    /// <summary>Drains outboxes and returns the raw invitation token delivered to <paramref name="email"/>.</summary>
    public async Task<string?> GetInvitationTokenAsync(string email)
    {
        await DrainOutboxesAsync();
        return Emails.TokenFor(email);
    }

    public async Task InitializeAsync() => await _sql.StartAsync();

    public new async Task DisposeAsync()
    {
        await _sql.DisposeAsync();
        await base.DisposeAsync();
    }
}
