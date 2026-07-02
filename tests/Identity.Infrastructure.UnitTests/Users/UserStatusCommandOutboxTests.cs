using System.Text.Json;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application.Abstractions;
using Identity.Application.Features.Users;
using Identity.Contracts;
using Identity.Domain.Roles;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Identity.Infrastructure.UnitTests.Users;

public sealed class UserStatusCommandOutboxTests
{
    [Theory]
    [InlineData(LinkedUserBlockingAction.Lock)]
    [InlineData(LinkedUserBlockingAction.Deactivate)]
    [InlineData(LinkedUserBlockingAction.Suspend)]
    public async Task Linked_user_blocking_actions_enqueue_typed_portal_deactivation(LinkedUserBlockingAction action)
    {
        await using var db = CreateDb();
        var user = await AddLinkedStationUserAsync(db);
        var currentUser = new TestCurrentUser(Guid.NewGuid());

        var result = action switch
        {
            LinkedUserBlockingAction.Lock => await new LockUserCommandHandler(db, currentUser, TimeProvider.System)
                .Handle(new LockUserCommand(user.Id), CancellationToken.None),
            LinkedUserBlockingAction.Deactivate => await new DeactivateUserCommandHandler(db, currentUser, TimeProvider.System)
                .Handle(new DeactivateUserCommand(user.Id), CancellationToken.None),
            LinkedUserBlockingAction.Suspend => await new SuspendUserCommandHandler(db, currentUser, TimeProvider.System)
                .Handle(new SuspendUserCommand(user.Id), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        result.IsSuccess.ShouldBeTrue();

        var message = await db.OutboxMessages.SingleAsync(m => m.Type.Contains(nameof(PortalUserDeactivated)));
        var integrationEvent = JsonSerializer.Deserialize<PortalUserDeactivated>(message.Content);

        integrationEvent.ShouldNotBeNull();
        integrationEvent.ExternalReferenceId.ShouldBe(user.ExternalReferenceId!.Value);
        integrationEvent.UserId.ShouldBe(user.Id);
        integrationEvent.UserType.ShouldBe(UserType.StationStaff);
        integrationEvent.ReleaseEmail.ShouldBeFalse();
    }

    private static IdentityDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-lifecycle-{Guid.NewGuid():N}")
            .Options);

    private static async Task<User> AddLinkedStationUserAsync(IdentityDbContext db)
    {
        var now = TimeProvider.System.GetUtcNow();
        var role = Role.Create("Station Staff", null, [], UserType.StationStaff, now).Value;
        var user = User.Invite(
            Email.Create("station.staff@example.com").Value,
            "Station Staff",
            role.Id,
            "invite-hash",
            now.AddHours(24),
            now,
            UserType.StationStaff,
            Guid.NewGuid()).Value;

        user.Activate("invite-hash", "password-hash", now).IsSuccess.ShouldBeTrue();

        db.Roles.Add(role);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public enum LinkedUserBlockingAction
    {
        Lock,
        Deactivate,
        Suspend
    }

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid? UserId => userId;

        public bool IsAuthenticated => true;
    }
}
