using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application.Authorization;
using Identity.Application.Features.Users;
using Identity.Contracts;
using Identity.Domain.Authorization;
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
        var managementPermissions = new[]
        {
            IdentityPermissions.Users.Lock,
            IdentityPermissions.Users.Deactivate,
            IdentityPermissions.Users.Suspend
        };
        var actor = await AddAdministratorAsync(db, managementPermissions);
        var currentUser = new TestUserContext(actor.Id, managementPermissions);
        var registry = new PermissionRegistry([new IdentityPermissionCatalog()]);

        var result = action switch
        {
            LinkedUserBlockingAction.Lock => await new LockUserCommandHandler(db, currentUser, registry, TimeProvider.System)
                .Handle(new LockUserCommand(user.Id), CancellationToken.None),
            LinkedUserBlockingAction.Deactivate => await new DeactivateUserCommandHandler(db, currentUser, registry, TimeProvider.System)
                .Handle(new DeactivateUserCommand(user.Id), CancellationToken.None),
            LinkedUserBlockingAction.Suspend => await new SuspendUserCommandHandler(db, currentUser, registry, TimeProvider.System)
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

    private static async Task<User> AddAdministratorAsync(
        IdentityDbContext db,
        IReadOnlyList<string> permissions)
    {
        var now = TimeProvider.System.GetUtcNow();
        var role = Role.Create(
            $"Lifecycle manager-{Guid.NewGuid():N}",
            null,
            permissions,
            UserType.SystemAdministrator,
            now).Value;
        var user = User.CreateActive(
            Email.Create($"lifecycle-manager-{Guid.NewGuid():N}@example.com").Value,
            "Lifecycle Manager",
            role.Id,
            "password-hash",
            now).Value;

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

    private sealed class TestUserContext(Guid userId, IReadOnlyList<string> permissions) : IUserContext
    {
        public Guid? UserId => userId;
        public bool IsAuthenticated => true;
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => permissions.Contains(permission);
    }
}
