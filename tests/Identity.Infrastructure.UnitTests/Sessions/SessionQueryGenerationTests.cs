using Identity.Application.Abstractions;
using Identity.Application.Features.Sessions;
using Identity.Domain.Sessions;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Identity.Infrastructure.UnitTests.Sessions;

public sealed class SessionQueryGenerationTests
{
    [Fact]
    public async Task Old_generation_session_is_not_reported_as_active()
    {
        await using var db = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(
                    $"identity-session-generation-{Guid.NewGuid():N}")
                .Options);
        var now = new DateTimeOffset(
            2026,
            7,
            24,
            21,
            0,
            0,
            TimeSpan.Zero);
        var user = User.CreateActive(
            Email.Create("session-generation@example.com").Value,
            "Session generation",
            Guid.NewGuid(),
            "password-hash",
            now).Value;
        var session = UserSession.Issue(
            user.Id,
            user.SecurityStamp,
            "hash:refresh",
            now.AddDays(1),
            now).Value;
        db.Users.Add(user);
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        user.RotateSecurityStamp(now.AddMinutes(1));
        await db.SaveChangesAsync();
        var clock = new FixedTimeProvider(now.AddMinutes(2));

        var all = await new GetUserSessionsQueryHandler(db, clock).Handle(
            new GetUserSessionsQuery(user.Id),
            CancellationToken.None);
        var active = await new GetUserSessionsQueryHandler(db, clock).Handle(
            new GetUserSessionsQuery(user.Id, ActiveOnly: true),
            CancellationToken.None);
        var mine = await new GetMySessionsQueryHandler(
                db,
                new TestCurrentUser(user.Id),
                new TestTokenService(),
                clock)
            .Handle(
                new GetMySessionsQuery("refresh"),
                CancellationToken.None);

        all.IsSuccess.ShouldBeTrue();
        all.Value.Items.ShouldHaveSingleItem().IsActive.ShouldBeFalse();
        active.IsSuccess.ShouldBeTrue();
        active.Value.Items.ShouldBeEmpty();
        active.Value.TotalCount.ShouldBe(0);
        mine.IsSuccess.ShouldBeTrue();
        mine.Value.Items.ShouldHaveSingleItem().IsActive.ShouldBeFalse();
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

        public RefreshToken CreateRefreshToken() =>
            throw new NotSupportedException();

        public string HashRefreshToken(string rawToken) =>
            $"hash:{rawToken}";

        public SecureToken CreateSecureToken() =>
            throw new NotSupportedException();

        public string HashToken(string rawToken) =>
            throw new NotSupportedException();

        public string CreateMfaChallengeToken(User user) =>
            throw new NotSupportedException();

        public MfaChallenge? ValidateMfaChallengeToken(string token) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
