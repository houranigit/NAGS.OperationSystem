using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Email;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Contracts.Email;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Identity.Domain.Sessions;
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
    public async Task Passwordless_bootstrap_admin_is_persisted_even_when_notifier_is_noop()
    {
        var databaseName = $"identity-seed-{Guid.NewGuid():N}";
        await using (var db = CreateDb(databaseName))
        {
            var seeder = CreateSeeder(
                db,
                new TestPermissionRegistry([IdentityPermissions.Users.View]),
                new NoopInvitationNotifier(),
                adminPassword: string.Empty);

            await seeder.SeedAsync();
        }

        await using var verification = CreateDb(databaseName);
        var user = await verification.Users.SingleAsync();
        user.Status.ShouldBe(UserStatus.Invited);
        user.InvitationToken.ShouldBe("hash:raw-bootstrap-token");
    }

    [Fact]
    public async Task Expired_bootstrap_invitation_requeue_is_persisted_with_noop_notifier()
    {
        var databaseName = $"identity-seed-{Guid.NewGuid():N}";
        await using (var db = CreateDb(databaseName))
        {
            var now = DateTimeOffset.UtcNow;
            var role = Role.Create(
                IdentitySeedIds.SystemAdminRoleName,
                "Protected",
                [IdentityPermissions.Users.View],
                UserType.SystemAdministrator,
                now,
                isSystem: true).Value;
            var user = User.Invite(
                Email.Create("bootstrap@example.com").Value,
                "Bootstrap Admin",
                role.Id,
                "expired-invitation-hash",
                now.AddMinutes(-1),
                now.AddDays(-1)).Value;
            db.Roles.Add(role);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDb(databaseName))
        {
            var seeder = CreateSeeder(
                db,
                new TestPermissionRegistry([IdentityPermissions.Users.View]),
                new NoopInvitationNotifier(),
                adminPassword: string.Empty);
            await seeder.SeedAsync();
        }

        await using var verification = CreateDb(databaseName);
        var refreshed = await verification.Users.SingleAsync();
        refreshed.InvitationToken.ShouldBe("hash:raw-bootstrap-token");
        refreshed.InvitationExpiresAtUtc.ShouldNotBeNull();
        refreshed.InvitationExpiresAtUtc.Value.ShouldBeGreaterThan(
            DateTimeOffset.UtcNow);
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
        Guid originalSecurityStamp;
        Guid sessionId;
        await using (var db = CreateDb(databaseName))
        {
            var seeder = CreateSeeder(
                db,
                new TestPermissionRegistry([IdentityPermissions.Users.View]),
                new NoopInvitationNotifier(),
                adminPassword: "Admin#12345");

            await seeder.SeedAsync();

            var seededUser = await db.Users.SingleAsync();
            originalSecurityStamp = seededUser.SecurityStamp;
            var session = UserSession.Issue(
                seededUser.Id,
                seededUser.SecurityStamp,
                $"seed-sync-session-{Guid.NewGuid():N}",
                DateTimeOffset.UtcNow.AddDays(1),
                DateTimeOffset.UtcNow).Value;
            sessionId = session.Id;
            db.Sessions.Add(session);
            await db.SaveChangesAsync();
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
        var verifiedUser = await verification.Users.SingleAsync();
        verifiedUser.SecurityStamp.ShouldNotBe(originalSecurityStamp);
        (await verification.Sessions.SingleAsync(session => session.Id == sessionId))
            .RevokedAtUtc.ShouldNotBeNull();
        (await verification.Roles.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Seed_removes_retired_permissions_from_custom_roles()
    {
        var databaseName = $"identity-seed-{Guid.NewGuid():N}";
        Guid customUserId;
        Guid customSessionId;
        Guid originalSecurityStamp;
        await using (var db = CreateDb(databaseName))
        {
            var now = DateTimeOffset.UtcNow;
            var role = Role.Create(
                "Legacy custom role",
                description: null,
                [IdentityPermissions.Roles.View, "identity.users.create"],
                UserType.SystemAdministrator,
                now).Value;
            var user = User.CreateActive(
                Email.Create("legacy-custom-role@example.com").Value,
                "Legacy Custom Role User",
                role.Id,
                "hashed:Admin#12345",
                now).Value;
            var session = UserSession.Issue(
                user.Id,
                user.SecurityStamp,
                $"retired-permission-session-{Guid.NewGuid():N}",
                now.AddDays(1),
                now).Value;

            customUserId = user.Id;
            customSessionId = session.Id;
            originalSecurityStamp = user.SecurityStamp;
            db.Roles.Add(role);
            db.Users.Add(user);
            db.Sessions.Add(session);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDb(databaseName))
        {
            var seeder = CreateSeeder(
                db,
                new TestPermissionRegistry([IdentityPermissions.Roles.View]),
                new NoopInvitationNotifier(),
                adminPassword: "Admin#12345");

            await seeder.SeedAsync();
        }

        await using var verification = CreateDb(databaseName);
        var customRole = await verification.Roles.SingleAsync(role => !role.IsSystem);
        customRole.Permissions.ShouldBe([IdentityPermissions.Roles.View]);
        var customUser = await verification.Users.SingleAsync(user => user.Id == customUserId);
        customUser.SecurityStamp.ShouldNotBe(originalSecurityStamp);
        (await verification.Sessions.SingleAsync(session => session.Id == customSessionId))
            .RevokedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Seed_preserves_an_existing_bootstrap_administrators_custom_role()
    {
        var databaseName = $"identity-seed-{Guid.NewGuid():N}";
        Guid customRoleId;
        await using (var db = CreateDb(databaseName))
        {
            var now = DateTimeOffset.UtcNow;
            var protectedRole = Role.Create(
                IdentitySeedIds.SystemAdminRoleName,
                "Protected break-glass role",
                [IdentityPermissions.Users.View],
                UserType.SystemAdministrator,
                now,
                isSystem: true).Value;
            var customRole = Role.Create(
                "Deliberately assigned bootstrap role",
                description: null,
                [IdentityPermissions.Users.View],
                UserType.SystemAdministrator,
                now).Value;
            var existing = User.CreateActive(
                Email.Create("bootstrap@example.com").Value,
                "Bootstrap Admin",
                customRole.Id,
                "hashed:Admin#12345",
                now).Value;
            var recoverableProtectedHolder = User.Invite(
                Email.Create("recoverable-admin@example.com").Value,
                "Recoverable Protected Admin",
                protectedRole.Id,
                "hash:invitation",
                now.AddDays(1),
                now).Value;
            customRoleId = customRole.Id;
            db.Roles.AddRange(protectedRole, customRole);
            db.Users.AddRange(existing, recoverableProtectedHolder);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDb(databaseName))
        {
            var seeder = CreateSeeder(
                db,
                new TestPermissionRegistry([IdentityPermissions.Users.View]),
                new NoopInvitationNotifier(),
                adminPassword: "Admin#12345");

            await seeder.SeedAsync();
        }

        await using var verification = CreateDb(databaseName);
        var user = await verification.Users.SingleAsync(
            candidate => candidate.Email.Value == "bootstrap@example.com");
        user.RoleId.ShouldBe(customRoleId);
        (await verification.Roles.CountAsync()).ShouldBe(2);
        (await verification.Users.CountAsync()).ShouldBe(2);
        (await verification.Roles.SingleAsync(role => role.IsSystem))
            .Id.ShouldNotBe(customRoleId);
    }

    [Fact]
    public async Task Seed_fails_when_preserving_the_bootstrap_custom_role_would_leave_no_recoverable_protected_administrator()
    {
        var databaseName = $"identity-seed-{Guid.NewGuid():N}";
        Guid customRoleId;
        await using (var db = CreateDb(databaseName))
        {
            var now = DateTimeOffset.UtcNow;
            var protectedRole = Role.Create(
                IdentitySeedIds.SystemAdminRoleName,
                "Protected break-glass role",
                [IdentityPermissions.Users.View],
                UserType.SystemAdministrator,
                now,
                isSystem: true).Value;
            var customRole = Role.Create(
                "Deliberately assigned bootstrap role",
                description: null,
                [IdentityPermissions.Users.View],
                UserType.SystemAdministrator,
                now).Value;
            var bootstrap = User.CreateActive(
                Email.Create("bootstrap@example.com").Value,
                "Bootstrap Admin",
                customRole.Id,
                "hashed:Admin#12345",
                now).Value;
            var deactivatedProtectedHolder = User.CreateActive(
                Email.Create("former-protected-admin@example.com").Value,
                "Former Protected Admin",
                protectedRole.Id,
                "hashed:Admin#12345",
                now).Value;
            deactivatedProtectedHolder.Deactivate(now.AddMinutes(1)).IsSuccess.ShouldBeTrue();

            customRoleId = customRole.Id;
            db.Roles.AddRange(protectedRole, customRole);
            db.Users.AddRange(bootstrap, deactivatedProtectedHolder);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDb(databaseName))
        {
            var seeder = CreateSeeder(
                db,
                new TestPermissionRegistry([IdentityPermissions.Users.View]),
                new NoopInvitationNotifier(),
                adminPassword: "Admin#12345");

            var exception = await Should.ThrowAsync<InvalidOperationException>(() => seeder.SeedAsync());
            exception.Message.ShouldContain("no live or recoverable System Administrator");
            exception.Message.ShouldContain("will not silently elevate");
        }

        await using var verification = CreateDb(databaseName);
        var bootstrapAfterFailure = await verification.Users.SingleAsync(
            candidate => candidate.Email.Value == "bootstrap@example.com");
        bootstrapAfterFailure.RoleId.ShouldBe(customRoleId);
        (await verification.Users.CountAsync()).ShouldBe(2);
        (await verification.Roles.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Seed_fails_when_the_configured_bootstrap_identity_was_demoted_and_no_protected_holder_remains()
    {
        var databaseName = $"identity-seed-{Guid.NewGuid():N}";
        await using (var db = CreateDb(databaseName))
        {
            var now = DateTimeOffset.UtcNow;
            var viewerRole = Role.Create(
                "Bootstrap Viewer",
                null,
                [IdentityPermissions.Users.View],
                UserType.ViewerOnly,
                now).Value;
            var demotedBootstrap = User.Invite(
                Email.Create("bootstrap@example.com").Value,
                "Bootstrap Viewer",
                viewerRole.Id,
                "hash:invitation",
                now.AddDays(1),
                now,
                UserType.ViewerOnly).Value;
            db.Roles.Add(viewerRole);
            db.Users.Add(demotedBootstrap);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDb(databaseName))
        {
            var seeder = CreateSeeder(
                db,
                new TestPermissionRegistry([IdentityPermissions.Users.View]),
                new NoopInvitationNotifier(),
                adminPassword: "Admin#12345");

            var exception = await Should.ThrowAsync<InvalidOperationException>(() => seeder.SeedAsync());
            exception.Message.ShouldContain("no live or recoverable System Administrator");
            exception.Message.ShouldContain("will not silently elevate");
        }

        await using var verification = CreateDb(databaseName);
        var bootstrap = await verification.Users.SingleAsync();
        bootstrap.UserType.ShouldBe(UserType.ViewerOnly);
        (await verification.Roles.CountAsync()).ShouldBe(2);
        (await verification.Users.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Seed_fails_closed_when_invalid_bootstrap_configuration_creates_no_protected_holder()
    {
        var databaseName = $"identity-seed-{Guid.NewGuid():N}";
        await using var db = CreateDb(databaseName);
        var seeder = CreateSeeder(
            db,
            new TestPermissionRegistry([IdentityPermissions.Users.View]),
            new NoopInvitationNotifier(),
            adminPassword: "Admin#12345",
            adminEmail: "not-an-email");

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => seeder.SeedAsync());

        exception.Message.ShouldContain("no live or recoverable System Administrator");
        (await db.Users.CountAsync()).ShouldBe(0);
        (await db.Roles.CountAsync()).ShouldBe(1);
    }

    private static IdentityDbContext CreateDb(string databaseName) =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);

    private static IdentityDataSeeder CreateSeeder(
        IdentityDbContext db,
        IPermissionRegistry permissionRegistry,
        IInvitationNotifier invitationNotifier,
        string adminPassword,
        string adminEmail = "bootstrap@example.com") =>
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
                    Email = adminEmail,
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
