using Identity.Domain.Sessions;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Shouldly;

namespace Identity.Infrastructure.UnitTests.Security;

public sealed class TokenSecurityValidatorTests
{
    [Fact]
    public async Task Previously_valid_token_is_rejected_immediately_after_security_stamp_rotation()
    {
        await using var db = CreateDb();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var now = TimeProvider.System.GetUtcNow();
        var user = User.CreateActive(
            Email.Create("token-current@example.com").Value,
            "Token Current",
            Guid.NewGuid(),
            "password-hash",
            now).Value;
        var originalStamp = user.SecurityStamp;
        var session = UserSession.Issue(
            user.Id,
            originalStamp,
            "refresh-hash",
            now.AddDays(1),
            now).Value;

        db.Users.Add(user);
        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        var validator = new TokenSecurityValidator(db, TimeProvider.System, cache);

        // Exercise the positive path before rotating the stamp. This deliberately detects any
        // future reintroduction of positive caching that would create a revocation grace period.
        (await validator.IsCurrentAsync(
            user.Id,
            originalStamp.ToString(),
            session.Id)).ShouldBeTrue();

        user.RotateSecurityStamp(now.AddMinutes(1));
        await db.SaveChangesAsync();

        (await validator.IsCurrentAsync(
            user.Id,
            originalStamp.ToString(),
            session.Id)).ShouldBeFalse();
    }

    private static IdentityDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-token-security-{Guid.NewGuid():N}")
            .Options);
}
