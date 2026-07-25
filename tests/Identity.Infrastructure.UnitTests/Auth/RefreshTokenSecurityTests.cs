using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application.Abstractions;
using Identity.Application.Features.Auth;
using Identity.Domain.Sessions;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Identity.Infrastructure.UnitTests.Auth;

public sealed class RefreshTokenSecurityTests
{
    [Fact]
    public async Task Refresh_rejects_and_revokes_a_session_from_an_old_security_stamp()
    {
        await using var db = CreateDb();
        var now = TimeProvider.System.GetUtcNow();
        var user = User.CreateActive(
            Email.Create("stale-refresh@example.com").Value,
            "Stale Refresh",
            Guid.NewGuid(),
            "password-hash",
            now).Value;
        var session = UserSession.Issue(
            user.Id,
            Guid.NewGuid(),
            "hash:raw-refresh",
            now.AddDays(1),
            now).Value;

        db.Users.Add(user);
        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        var handler = new RefreshTokenCommandHandler(
            db,
            new TestTokenService(),
            EmptyPermissionRegistry.Instance,
            TimeProvider.System);

        var result = await handler.Handle(
            new RefreshTokenCommand("raw-refresh", null, null),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.Auth.InvalidRefreshToken");
        session.RevokedAtUtc.ShouldNotBeNull();
        db.Sessions.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Logout_with_a_rotated_token_revokes_the_active_family_successor()
    {
        await using var db = CreateDb();
        var now = TimeProvider.System.GetUtcNow();
        var predecessor = UserSession.Issue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "hash:old-refresh",
            now.AddDays(1),
            now).Value;
        var successor = predecessor.ContinueWith(
            "hash:new-refresh",
            now.AddDays(1),
            now.AddMinutes(1)).Value;
        predecessor.Revoke(now.AddMinutes(1));
        db.Sessions.AddRange(predecessor, successor);
        await db.SaveChangesAsync();

        var result = await new LogoutCommandHandler(
                db,
                new TestTokenService(),
                TimeProvider.System)
            .Handle(
                new LogoutCommand("old-refresh"),
                CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        successor.RevokedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Refresh_reports_a_consumed_predecessor_separately_from_an_unknown_token()
    {
        await using var db = CreateDb();
        var now = TimeProvider.System.GetUtcNow();
        var session = UserSession.Issue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "hash:consumed-refresh",
            now.AddDays(1),
            now).Value;
        session.Revoke(now.AddMinutes(1));
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        var handler = new RefreshTokenCommandHandler(
            db,
            new TestTokenService(),
            EmptyPermissionRegistry.Instance,
            TimeProvider.System);

        var consumed = await handler.Handle(
            new RefreshTokenCommand("consumed-refresh", null, null),
            CancellationToken.None);
        var unknown = await handler.Handle(
            new RefreshTokenCommand("unknown-refresh", null, null),
            CancellationToken.None);

        consumed.IsFailure.ShouldBeTrue();
        consumed.Error.Code.ShouldBe(
            RefreshTokenCommandHandler.ConsumedTokenErrorCode);
        unknown.IsFailure.ShouldBeTrue();
        unknown.Error.Code.ShouldBe("Identity.Auth.InvalidRefreshToken");
    }

    private static IdentityDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-refresh-security-{Guid.NewGuid():N}")
            .Options);

    private sealed class TestTokenService : ITokenService
    {
        public AccessToken CreateAccessToken(
            User user,
            IReadOnlyCollection<string> permissions,
            Guid sessionId) =>
            throw new InvalidOperationException("A stale session must not issue an access token.");

        public RefreshToken CreateRefreshToken() =>
            throw new InvalidOperationException("A stale session must not rotate its refresh token.");

        public string HashRefreshToken(string rawToken) => $"hash:{rawToken}";

        public SecureToken CreateSecureToken() =>
            new("secure", "hash:secure");

        public string HashToken(string rawToken) => $"hash:{rawToken}";

        public string CreateMfaChallengeToken(User user) =>
            throw new NotSupportedException();

        public MfaChallenge? ValidateMfaChallengeToken(string token) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyPermissionRegistry : IPermissionRegistry
    {
        public static readonly EmptyPermissionRegistry Instance = new();

        public IReadOnlyList<PermissionDescriptor> All => [];

        public bool IsKnown(string permission) => false;

        public bool IsCompatibleWith(string permission, UserType userType) => false;

        public IReadOnlyList<string> CompatiblePermissions(UserType userType) => [];
    }
}
