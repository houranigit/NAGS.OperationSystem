using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Email;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Contracts.Email;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Domain.Authorization;
using Identity.Domain.Users;
using Identity.Infrastructure.Notifications;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using System.Text;
using System.Text.Json;

namespace Identity.Infrastructure.UnitTests.Seeding;

public sealed class IdentityDataSeederTests
{
    [Fact]
    public async Task Passwordless_bootstrap_admin_persists_invited_admin_and_invitation_outbox_when_queueing_succeeds()
    {
        var databaseName = $"identity-seed-{Guid.NewGuid():N}";
        await using var db = CreateDb(databaseName);
        var seeder = CreateSeeder(
            db,
            new TestPermissionRegistry([IdentityPermissions.Users.View, IdentityPermissions.Roles.View]),
            InvitationNotifier(db),
            adminPassword: string.Empty);

        await seeder.SeedAsync();

        await using var verification = CreateDb(databaseName);
        var user = await verification.Users.SingleAsync();
        user.Email.Value.ShouldBe("bootstrap@example.com");
        user.DisplayName.ShouldBe("Bootstrap Admin");
        user.UserType.ShouldBe(UserType.SystemAdministrator);
        user.Status.ShouldBe(UserStatus.Invited);
        user.InvitationToken.ShouldBe("hash:raw-bootstrap-token");
        user.InvitationExpiresAtUtc.ShouldNotBeNull();

        var role = await verification.Roles.SingleAsync();
        role.IsSystem.ShouldBeTrue();
        role.CompatibleUserType.ShouldBe(UserType.SystemAdministrator);
        role.Permissions.ShouldBe(["identity.users.view", "identity.roles.view"], ignoreOrder: true);
        user.RoleId.ShouldBe(role.Id);

        var outbox = await verification.OutboxMessages.SingleAsync();
        outbox.Type.ShouldContain(nameof(EmailDeliveryRequested));
        outbox.Content.ShouldNotContain("raw-bootstrap-token");
        var email = JsonSerializer.Deserialize<EmailDeliveryRequested>(outbox.Content);
        email.ShouldNotBeNull();
        email!.ToEmail.ShouldBe("bootstrap@example.com");
        email.Kind.ShouldBe("invitation");
    }

    [Fact]
    public async Task Passwordless_bootstrap_admin_is_not_persisted_when_invitation_queueing_fails()
    {
        var databaseName = $"identity-seed-{Guid.NewGuid():N}";
        await using var db = CreateDb(databaseName);
        var seeder = CreateSeeder(db, new TestPermissionRegistry(), new ThrowingInvitationNotifier(), adminPassword: string.Empty);

        await Should.ThrowAsync<InvalidOperationException>(() => seeder.SeedAsync());

        await using var verification = CreateDb(databaseName);
        (await verification.Roles.CountAsync()).ShouldBe(1);
        (await verification.Users.CountAsync()).ShouldBe(0);
        (await verification.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Seed_is_idempotent_and_syncs_system_admin_role_permissions()
    {
        var databaseName = $"identity-seed-{Guid.NewGuid():N}";
        await using (var db = CreateDb(databaseName))
        {
            var seeder = CreateSeeder(
                db,
                new TestPermissionRegistry([IdentityPermissions.Users.View]),
                new NoopInvitationNotifier(),
                adminPassword: "Admin#12345");

            await seeder.SeedAsync();
        }

        await using (var db = CreateDb(databaseName))
        {
            var seeder = CreateSeeder(
                db,
                new TestPermissionRegistry([IdentityPermissions.Users.View, IdentityPermissions.Roles.View]),
                new NoopInvitationNotifier(),
                adminPassword: "Admin#12345");

            await seeder.SeedAsync();
        }

        await using var verification = CreateDb(databaseName);
        var role = await verification.Roles.SingleAsync();
        role.Permissions.ShouldBe([IdentityPermissions.Users.View, IdentityPermissions.Roles.View], ignoreOrder: true);
        (await verification.Users.CountAsync()).ShouldBe(1);
        (await verification.Roles.CountAsync()).ShouldBe(1);
    }

    private static IdentityDbContext CreateDb(string databaseName) =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);

    private static IdentityDataSeeder CreateSeeder(
        IdentityDbContext db,
        IPermissionRegistry permissionRegistry,
        IInvitationNotifier invitationNotifier,
        string adminPassword) =>
        new(
            db,
            new TestPasswordHasher(),
            permissionRegistry,
            new TestTokenService(),
            invitationNotifier,
            TimeProvider.System,
            Options.Create(new IdentityModuleOptions
            {
                Admin = new AdminBootstrapOptions
                {
                    Email = "bootstrap@example.com",
                    DisplayName = "Bootstrap Admin",
                    Password = adminPassword
                }
            }),
            NullLogger<IdentityDataSeeder>.Instance);

    private static EmailInvitationNotifier InvitationNotifier(IdentityDbContext db) =>
        new(
            db,
            new TestEmailContentProtector(),
            new DisabledEmailSender(),
            new TestHostEnvironment(Environments.Production),
            NullLogger<EmailInvitationNotifier>.Instance,
            Options.Create(new IdentityModuleOptions
            {
                Admin = new AdminBootstrapOptions
                {
                    Email = "bootstrap@example.com",
                    DisplayName = "Bootstrap Admin"
                }
            }));

    private sealed class ThrowingInvitationNotifier : IInvitationNotifier
    {
        public Task SendInvitationAsync(
            string email,
            string displayName,
            Guid userId,
            string invitationToken,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Invitation queue unavailable.");
    }

    private sealed class NoopInvitationNotifier : IInvitationNotifier
    {
        public Task SendInvitationAsync(
            string email,
            string displayName,
            Guid userId,
            string invitationToken,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";

        public bool Verify(string passwordHash, string providedPassword) =>
            passwordHash == Hash(providedPassword);
    }

    private sealed class TestTokenService : ITokenService
    {
        public AccessToken CreateAccessToken(User user, IReadOnlyCollection<string> permissions, Guid sessionId) =>
            throw new NotSupportedException();

        public RefreshToken CreateRefreshToken() =>
            throw new NotSupportedException();

        public string HashRefreshToken(string rawToken) => $"hash:{rawToken}";

        public SecureToken CreateSecureToken() => new("raw-bootstrap-token", "hash:raw-bootstrap-token");

        public string HashToken(string rawToken) => $"hash:{rawToken}";

        public string CreateMfaChallengeToken(User user) =>
            throw new NotSupportedException();

        public MfaChallenge? ValidateMfaChallengeToken(string token) =>
            throw new NotSupportedException();
    }

    private sealed class TestPermissionRegistry : IPermissionRegistry
    {
        public TestPermissionRegistry(IReadOnlyList<string>? permissions = null)
        {
            All = (permissions ?? [IdentityPermissions.Users.View])
                .Select(permission => new PermissionDescriptor(permission, [UserType.SystemAdministrator]))
                .ToList();
        }

        public IReadOnlyList<PermissionDescriptor> All { get; }

        public bool IsKnown(string permission) => All.Any(p => p.Code == permission);

        public bool IsCompatibleWith(string permission, UserType userType) =>
            All.Any(p => p.Code == permission && p.IsCompatibleWith(userType));

        public IReadOnlyList<string> CompatiblePermissions(UserType userType) =>
            All.Where(p => p.IsCompatibleWith(userType)).Select(p => p.Code).ToList();
    }

    private sealed class TestEmailContentProtector : IEmailContentProtector
    {
        public string Protect(string plaintext) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));

        public string Unprotect(string protectedValue) =>
            Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue));
    }

    private sealed class DisabledEmailSender : IEmailSender
    {
        public bool IsEnabled => false;

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Identity.Infrastructure.UnitTests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
