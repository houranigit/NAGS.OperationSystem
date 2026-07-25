using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application.Authorization;
using Identity.Application.Features.Users;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Identity.Infrastructure.UnitTests.Users;

public sealed class UserLifecycleProtectedAdminTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Custom_administrator_does_not_satisfy_the_lock_break_glass_guard()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db, addSecondProtectedHolder: false);
        var handler = new LockUserCommandHandler(
            db,
            Context(seed.OtherAdministrator.Id, IdentityPermissions.Users.Lock),
            Registry(),
            new FixedTimeProvider(Now));

        var result = await handler.Handle(
            new LockUserCommand(seed.ProtectedAdministrator.Id),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.LastAdmin");
        seed.ProtectedAdministrator.IsLockedOut(Now).ShouldBeFalse();
    }

    [Fact]
    public async Task Another_sign_in_capable_protected_holder_allows_lock()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db, addSecondProtectedHolder: true);
        var handler = new LockUserCommandHandler(
            db,
            Context(seed.OtherAdministrator.Id, IdentityPermissions.Users.Lock),
            Registry(),
            new FixedTimeProvider(Now));

        var result = await handler.Handle(
            new LockUserCommand(seed.ProtectedAdministrator.Id),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        seed.ProtectedAdministrator.IsLockedOut(Now.AddYears(1)).ShouldBeTrue();
    }

    [Fact]
    public async Task Temporarily_locked_last_live_protected_holder_cannot_be_permanently_locked()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db, addSecondProtectedHolder: false);
        seed.ProtectedAdministrator.RecordFailedSignIn(
            1,
            TimeSpan.FromMinutes(15),
            Now,
            allowLockout: true).ShouldBeTrue();
        await db.SaveChangesAsync();
        var temporaryLockout = seed.ProtectedAdministrator.LockoutEndUtc;
        var handler = new LockUserCommandHandler(
            db,
            Context(seed.OtherAdministrator.Id, IdentityPermissions.Users.Lock),
            Registry(),
            new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.Handle(
            new LockUserCommand(seed.ProtectedAdministrator.Id),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.LastAdmin");
        seed.ProtectedAdministrator.LockoutEndUtc.ShouldBe(temporaryLockout);
        seed.ProtectedAdministrator.LockoutEndUtc.ShouldNotBe(DateTimeOffset.MaxValue);
    }

    [Fact]
    public async Task Invited_last_live_protected_holder_cannot_be_suspended()
    {
        await using var db = CreateDb();
        var managementPermissions = new[]
        {
            IdentityPermissions.Users.Suspend
        };
        var protectedRole = Role.Create(
            $"Protected-invited-{Guid.NewGuid():N}",
            null,
            managementPermissions,
            UserType.SystemAdministrator,
            Now,
            isSystem: true).Value;
        var customRole = Role.Create(
            $"Custom-actor-{Guid.NewGuid():N}",
            null,
            managementPermissions,
            UserType.SystemAdministrator,
            Now).Value;
        var invited = User.Invite(
            Email.Create($"invited-{Guid.NewGuid():N}@nags.sa").Value,
            "Invited protected admin",
            protectedRole.Id,
            "invitation-hash",
            Now.AddDays(1),
            Now).Value;
        var actor = CreateAdministrator("suspend-actor", customRole.Id);
        db.Roles.AddRange(protectedRole, customRole);
        db.Users.AddRange(invited, actor);
        await db.SaveChangesAsync();
        var handler = new SuspendUserCommandHandler(
            db,
            Context(actor.Id, IdentityPermissions.Users.Suspend),
            Registry(),
            new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.Handle(
            new SuspendUserCommand(invited.Id),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.LastAdmin");
        invited.Status.ShouldBe(UserStatus.Invited);
    }

    [Fact]
    public async Task Suspended_last_live_protected_holder_cannot_be_deactivated()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db, addSecondProtectedHolder: false);
        seed.ProtectedAdministrator.Suspend(Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        var handler = new DeactivateUserCommandHandler(
            db,
            Context(seed.OtherAdministrator.Id, IdentityPermissions.Users.Deactivate),
            Registry(),
            new FixedTimeProvider(Now.AddMinutes(2)));

        var result = await handler.Handle(
            new DeactivateUserCommand(seed.ProtectedAdministrator.Id),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.LastAdmin");
        seed.ProtectedAdministrator.Status.ShouldBe(UserStatus.Suspended);
    }

    [Fact]
    public async Task Lifecycle_action_revalidates_the_actors_live_role_after_authorization()
    {
        await using var db = CreateDb();
        var actorRole = Role.Create(
            $"Read-only actor-{Guid.NewGuid():N}",
            null,
            [IdentityPermissions.Users.View],
            UserType.SystemAdministrator,
            Now).Value;
        var targetRole = Role.Create(
            $"Target role-{Guid.NewGuid():N}",
            null,
            [],
            UserType.SystemAdministrator,
            Now).Value;
        var actor = CreateAdministrator("stale-actor", actorRole.Id);
        var target = CreateAdministrator("lifecycle-target", targetRole.Id);
        db.Roles.AddRange(actorRole, targetRole);
        db.Users.AddRange(actor, target);
        await db.SaveChangesAsync();

        var handler = new LockUserCommandHandler(
            db,
            Context(actor.Id, IdentityPermissions.Users.Lock),
            Registry(),
            new FixedTimeProvider(Now));

        var result = await handler.Handle(
            new LockUserCommand(target.Id),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.AccessManagementForbidden");
        target.IsLockedOut(Now.AddMinutes(1)).ShouldBeFalse();
    }

    private static IdentityDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-lifecycle-{Guid.NewGuid():N}")
            .Options);

    private static async Task<Seed> SeedAsync(
        IdentityDbContext db,
        bool addSecondProtectedHolder)
    {
        var managementPermissions = new[]
        {
            IdentityPermissions.Users.Lock,
            IdentityPermissions.Users.Deactivate,
            IdentityPermissions.Users.Suspend
        };
        var protectedRole = Role.Create(
            $"Protected-{Guid.NewGuid():N}",
            null,
            managementPermissions,
            UserType.SystemAdministrator,
            Now,
            isSystem: true).Value;
        var customRole = Role.Create(
            $"Custom-{Guid.NewGuid():N}",
            null,
            managementPermissions,
            UserType.SystemAdministrator,
            Now).Value;
        var protectedAdministrator = CreateAdministrator(
            "protected",
            protectedRole.Id);
        var otherAdministrator = CreateAdministrator(
            "other",
            addSecondProtectedHolder ? protectedRole.Id : customRole.Id);

        db.Roles.AddRange(protectedRole, customRole);
        db.Users.AddRange(protectedAdministrator, otherAdministrator);
        await db.SaveChangesAsync();
        return new Seed(protectedAdministrator, otherAdministrator);
    }

    private static User CreateAdministrator(string prefix, Guid roleId) =>
        User.CreateActive(
            Email.Create($"{prefix}-{Guid.NewGuid():N}@nags.sa").Value,
            prefix,
            roleId,
            "hash",
            Now).Value;

    private sealed record Seed(
        User ProtectedAdministrator,
        User OtherAdministrator);

    private static PermissionRegistry Registry() =>
        new([new IdentityPermissionCatalog()]);

    private static TestUserContext Context(Guid userId, params string[] permissions) =>
        new(userId, permissions);

    private sealed class TestUserContext(Guid userId, IReadOnlyList<string> permissions) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => userId;
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => permissions.Contains(permission);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
