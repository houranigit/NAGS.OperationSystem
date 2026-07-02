using Identity.Application.Abstractions;
using Identity.Application.Features.Users;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Identity.IntegrationTests;

public class UserLifecycleGuardTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    [Fact]
    public async Task Lock_rejects_last_sign_in_capable_admin_when_other_admin_is_locked()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var role = await db.Roles.SingleAsync(r => r.IsSystem);
        var seededAdmin = await db.Users.SingleAsync(u => u.Email.Value == IdentityApiFactory.AdminEmail);

        var lockedAdmin = User.CreateActive(
            Email.Create($"locked-admin-{Guid.NewGuid():N}@nags.sa").Value,
            "Locked Admin",
            role.Id,
            "hashed-password",
            now).Value;
        lockedAdmin.Lock(now);

        db.Users.Add(lockedAdmin);
        await db.SaveChangesAsync();

        var handler = new LockUserCommandHandler(
            db,
            new TestCurrentUser(Guid.NewGuid()),
            new FixedTimeProvider(now.AddMinutes(1)));

        var result = await handler.Handle(new LockUserCommand(seededAdmin.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.LastAdmin");
        seededAdmin.IsLockedOut(now.AddMinutes(2)).ShouldBeFalse();
    }

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;

        public bool IsAuthenticated => true;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
