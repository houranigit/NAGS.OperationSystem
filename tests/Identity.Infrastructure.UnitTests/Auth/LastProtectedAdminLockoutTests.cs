using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Application.Features.Auth;
using Identity.Domain.Roles;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Identity.Infrastructure.UnitTests.Auth;

public sealed class LastProtectedAdminLockoutTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Failed_password_cannot_auto_lock_the_last_sign_in_capable_protected_administrator()
    {
        await using var db = CreateDb();
        var role = ProtectedRole();
        var user = Administrator("last-protected", role.Id);
        var originalStamp = user.SecurityStamp;
        db.Roles.Add(role);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new LoginCommand(user.Email.Value, "wrong-password", null, null),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.Auth.InvalidCredentials");

        db.ChangeTracker.Clear();
        var persisted = await db.Users.SingleAsync();
        persisted.IsLockedOut(Now).ShouldBeFalse();
        persisted.AccessFailedCount.ShouldBe(0);
        persisted.SecurityStamp.ShouldBe(originalStamp);
    }

    [Fact]
    public async Task Failed_password_auto_locks_when_another_sign_in_capable_protected_administrator_exists()
    {
        await using var db = CreateDb();
        var role = ProtectedRole();
        var target = Administrator("target", role.Id);
        var other = Administrator("other", role.Id);
        var originalStamp = target.SecurityStamp;
        db.Roles.Add(role);
        db.Users.AddRange(target, other);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new LoginCommand(target.Email.Value, "wrong-password", null, null),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.Auth.InvalidCredentials");

        db.ChangeTracker.Clear();
        var persisted = await db.Users.SingleAsync(user => user.Id == target.Id);
        persisted.IsLockedOut(Now).ShouldBeTrue();
        persisted.AccessFailedCount.ShouldBe(0);
        persisted.SecurityStamp.ShouldNotBe(originalStamp);
    }

    private static IdentityDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-last-admin-lockout-{Guid.NewGuid():N}")
            .Options);

    private static Role ProtectedRole() =>
        Role.Create(
            $"Protected-{Guid.NewGuid():N}",
            null,
            [],
            UserType.SystemAdministrator,
            Now,
            isSystem: true).Value;

    private static User Administrator(string prefix, Guid roleId) =>
        User.CreateActive(
            Email.Create($"{prefix}-{Guid.NewGuid():N}@nags.sa").Value,
            prefix,
            roleId,
            TestPasswordHasher.HashValue("correct-password"),
            Now).Value;

    private static LoginCommandHandler Handler(IdentityDbContext db) =>
        new(
            db,
            new TestPasswordHasher(),
            new UnusedTokenService(),
            new UnusedMfaSecretProtector(),
            new PermissionRegistry([new IdentityPermissionCatalog()]),
            new FixedTimeProvider(Now),
            Options.Create(new IdentityModuleOptions
            {
                MaxFailedSignInAttempts = 1,
                LockoutMinutes = 15
            }));

    private sealed class TestPasswordHasher : IPasswordHasher
    {
        public static string HashValue(string password) => $"hash:{password}";

        public string Hash(string password) => HashValue(password);

        public bool Verify(string passwordHash, string providedPassword) =>
            passwordHash == HashValue(providedPassword);
    }

    private sealed class UnusedMfaSecretProtector : IMfaSecretProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string protectedValue) => protectedValue;

        public bool TryUnprotect(string protectedValue, out string plaintext)
        {
            plaintext = protectedValue;
            return true;
        }
    }

    private sealed class UnusedTokenService : ITokenService
    {
        public AccessToken CreateAccessToken(
            User user,
            IReadOnlyCollection<string> permissions,
            Guid sessionId) =>
            throw new NotSupportedException();

        public RefreshToken CreateRefreshToken() => throw new NotSupportedException();

        public string HashRefreshToken(string rawToken) => throw new NotSupportedException();

        public SecureToken CreateSecureToken() => throw new NotSupportedException();

        public string HashToken(string rawToken) => throw new NotSupportedException();

        public string CreateMfaChallengeToken(User user) => throw new NotSupportedException();

        public MfaChallenge? ValidateMfaChallengeToken(string token) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
