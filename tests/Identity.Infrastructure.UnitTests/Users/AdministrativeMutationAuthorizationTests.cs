using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application.Authorization;
using Identity.Application.Features.Sessions;
using Identity.Application.Features.Users;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Identity.Domain.Sessions;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Identity.Infrastructure.UnitTests.Users;

public sealed class AdministrativeMutationAuthorizationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Profile_update_revalidates_the_actors_live_permission()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var handler = new UpdateUserCommandHandler(
            db,
            Context(seed.Actor.Id, IdentityPermissions.Users.Update),
            Registry(),
            new FixedTimeProvider(Now));

        var result = await handler.Handle(
            new UpdateUserCommand(seed.Target.Id, "Changed by stale actor"),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.AccessManagementForbidden");
        seed.Target.DisplayName.ShouldBe("Target");
    }

    [Fact]
    public async Task Administrative_session_revocation_revalidates_the_actors_live_permission()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var session = UserSession.Issue(
            seed.Target.Id,
            seed.Target.SecurityStamp,
            $"administrative-session-{Guid.NewGuid():N}",
            Now.AddDays(1),
            Now).Value;
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        var handler = new RevokeSessionCommandHandler(
            db,
            Context(seed.Actor.Id, IdentityPermissions.Sessions.Revoke),
            Registry(),
            new FixedTimeProvider(Now));

        var result = await handler.Handle(
            new RevokeSessionCommand(session.Id),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.User.AccessManagementForbidden");
        session.RevokedAtUtc.ShouldBeNull();
    }

    private static IdentityDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-administrative-authorization-{Guid.NewGuid():N}")
            .Options);

    private static async Task<(User Actor, User Target)> SeedAsync(IdentityDbContext db)
    {
        var actorRole = Role.Create(
            $"Read-only actor-{Guid.NewGuid():N}",
            null,
            [IdentityPermissions.Users.View],
            UserType.SystemAdministrator,
            Now).Value;
        var targetRole = Role.Create(
            $"Target-{Guid.NewGuid():N}",
            null,
            [],
            UserType.SystemAdministrator,
            Now).Value;
        var actor = Administrator("actor", actorRole.Id);
        var target = Administrator("target", targetRole.Id);

        db.Roles.AddRange(actorRole, targetRole);
        db.Users.AddRange(actor, target);
        await db.SaveChangesAsync();
        return (actor, target);
    }

    private static User Administrator(string prefix, Guid roleId) =>
        User.CreateActive(
            Email.Create($"{prefix}-{Guid.NewGuid():N}@nags.sa").Value,
            prefix == "target" ? "Target" : "Actor",
            roleId,
            "hash",
            Now).Value;

    private static PermissionRegistry Registry() =>
        new([new IdentityPermissionCatalog()]);

    private static TestUserContext Context(Guid userId, params string[] permissions) =>
        new(userId, permissions);

    private sealed class TestUserContext(Guid userId, IReadOnlyList<string> permissions)
        : IUserContext
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
