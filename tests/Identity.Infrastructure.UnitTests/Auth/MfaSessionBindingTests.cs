using Identity.Application.Abstractions;
using Identity.Application.Features.Auth;
using Identity.Domain.Sessions;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Identity.Infrastructure.UnitTests.Auth;

public sealed class MfaSessionBindingTests
{
    [Fact]
    public async Task Self_service_mfa_disable_rebinds_only_the_presented_session()
    {
        await using var db = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase($"identity-mfa-session-{Guid.NewGuid():N}")
                .Options);
        var now = DateTimeOffset.UtcNow;
        var user = User.CreateActive(
            Email.Create("mfa-session@example.com").Value,
            "MFA Session",
            Guid.NewGuid(),
            "password-hash",
            now).Value;
        user.BeginMfaEnrollment("protected-secret", now).IsSuccess.ShouldBeTrue();
        user.ConfirmMfaEnrollment(["recovery-code"], now).IsSuccess.ShouldBeTrue();

        var current = UserSession.Issue(
            user.Id,
            user.SecurityStamp,
            "hash:current-refresh",
            now.AddDays(1),
            now).Value;
        var other = UserSession.Issue(
            user.Id,
            user.SecurityStamp,
            "hash:other-refresh",
            now.AddDays(1),
            now).Value;
        var previousStamp = user.SecurityStamp;

        db.Users.Add(user);
        db.Sessions.AddRange(current, other);
        await db.SaveChangesAsync();

        var result = await new DisableMfaCommandHandler(
                db,
                new TestCurrentUser(user.Id),
                new TestTokenService(),
                new FixedTimeProvider(now.AddMinutes(1)))
            .Handle(
                new DisableMfaCommand("current-refresh"),
                CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        user.MfaEnabled.ShouldBeFalse();
        user.SecurityStamp.ShouldNotBe(previousStamp);
        current.SecurityStamp.ShouldBe(user.SecurityStamp);
        other.SecurityStamp.ShouldBe(previousStamp);
    }

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid? UserId => userId;
        public bool IsAuthenticated => true;
    }

    private sealed class TestTokenService : ITokenService
    {
        public AccessToken CreateAccessToken(
            User user,
            IReadOnlyCollection<string> permissions,
            Guid sessionId) =>
            throw new NotSupportedException();

        public RefreshToken CreateRefreshToken() => throw new NotSupportedException();
        public string HashRefreshToken(string rawToken) => $"hash:{rawToken}";
        public SecureToken CreateSecureToken() => throw new NotSupportedException();
        public string HashToken(string rawToken) => $"hash:{rawToken}";
        public string CreateMfaChallengeToken(User user) => throw new NotSupportedException();
        public MfaChallenge? ValidateMfaChallengeToken(string token) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
