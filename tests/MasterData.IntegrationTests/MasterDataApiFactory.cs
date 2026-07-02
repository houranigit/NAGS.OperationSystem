using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Infrastructure.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    private string? _adminMfaSecret;

    public const string AdminEmail = "admin@nags.sa";
    public const string AdminPassword = "Admin#12345";
    public const string IdentityBase = "/api/v1/identity";
    public const string Base = "/api/v1/masterdata";

    /// <summary>Captures emails at the delivery boundary so tests can read activation tokens.</summary>
    public CapturingEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(Emails)));

        var connectionString = _sql.GetConnectionString().Replace("Database=master", "Database=MasterDataTests");

        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Identity:DemoData:Enabled", "false");
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
                // Tests share a client IP and authenticate frequently; relax the anonymous auth limit.
                ["Security:RateLimit:AnonymousAuthPermitLimit"] = "1000000"
            });
        });
    }

    public async Task<HttpClient> CreateAuthenticatedAdminClientAsync()
    {
        var client = CreateClient();
        var token = await LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await EnsureAdminMfaAsync(client);
        return client;
    }

    private async Task<string> LoginAsAdminAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync($"{IdentityBase}/auth/login",
            new { email = AdminEmail, password = AdminPassword });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        login.ShouldNotBeNull();

        if (login!.MfaRequired)
            return await CompleteAdminMfaLoginAsync(client, login.MfaToken);

        login.AccessToken.ShouldNotBeNullOrWhiteSpace();
        return login.AccessToken!;
    }

    private async Task EnsureAdminMfaAsync(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<MeResponse>($"{IdentityBase}/me");
        me.ShouldNotBeNull();

        if (me!.MfaEnrollmentRequired)
        {
            var enrollmentResponse = await client.PostAsync($"{IdentityBase}/auth/mfa/enroll", content: null);
            enrollmentResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var enrollment = await enrollmentResponse.Content.ReadFromJsonAsync<EnrollmentResponse>();
            enrollment.ShouldNotBeNull();

            _adminMfaSecret = enrollment!.Secret;

            var confirm = await client.PostAsJsonAsync($"{IdentityBase}/auth/mfa/confirm", new { code = Totp(enrollment.Secret) });
            confirm.StatusCode.ShouldBe(HttpStatusCode.OK);

            var refresh = await client.PostAsync($"{IdentityBase}/auth/refresh", content: null);
            refresh.StatusCode.ShouldBe(HttpStatusCode.OK);
            var token = await refresh.Content.ReadFromJsonAsync<TokenResponse>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

            me = await client.GetFromJsonAsync<MeResponse>($"{IdentityBase}/me");
        }

        me!.MfaEnabled.ShouldBeTrue();
        me.Permissions.ShouldNotBeEmpty();
    }

    private async Task<string> CompleteAdminMfaLoginAsync(HttpClient client, string? mfaToken)
    {
        mfaToken.ShouldNotBeNullOrWhiteSpace();

        if (string.IsNullOrWhiteSpace(_adminMfaSecret))
            throw new InvalidOperationException("The seeded admin has MFA enabled, but the test helper does not know its MFA secret.");

        var response = await client.PostAsJsonAsync($"{IdentityBase}/auth/login/mfa", new { mfaToken, code = Totp(_adminMfaSecret) });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return token!.AccessToken;
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

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);
    private sealed record LoginResponse(bool MfaRequired, string? MfaToken, string? AccessToken, DateTimeOffset? ExpiresAtUtc);
    private sealed record EnrollmentResponse(string Secret, string OtpAuthUri);
    private sealed record MeResponse(bool MfaEnabled, bool MfaEnrollmentRequired, List<string> Permissions);

    private static string Totp(string base32Secret)
    {
        var key = Base32Decode(base32Secret);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counterBytes);

        var hash = HMACSHA1.HashData(key, counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);

        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var value = 0;
        var output = new List<byte>();

        foreach (var c in input.TrimEnd('=').ToUpperInvariant())
        {
            var index = alphabet.IndexOf(c);
            if (index < 0)
                continue;

            value = (value << 5) | index;
            bits += 5;

            if (bits < 8)
                continue;

            output.Add((byte)((value >> (bits - 8)) & 0xFF));
            bits -= 8;
        }

        return output.ToArray();
    }
}
