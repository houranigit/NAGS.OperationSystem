using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application.Features.Roles;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Identity.Infrastructure.UnitTests.Roles;

public sealed class RoleOptionsQueryTests
{
    [Fact]
    public async Task Assignable_options_exclude_roles_above_the_callers_permission_ceiling()
    {
        await using var db = CreateDb();
        var allowed = CreateRole("Allowed role", [IdentityPermissions.Roles.View]);
        var elevated = CreateRole("Elevated role", [
            IdentityPermissions.Roles.View,
            IdentityPermissions.Users.Update
        ]);
        db.Roles.AddRange(allowed, elevated);
        await db.SaveChangesAsync();

        var handler = new GetRoleOptionsQueryHandler(
            db,
            new TestUserContext([IdentityPermissions.Roles.View]));

        var result = await handler.Handle(
            new GetRoleOptionsQuery(UserType.SystemAdministrator, AssignableOnly: true),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(option => option.Id).ShouldBe([allowed.Id]);
    }

    [Fact]
    public async Task Ordinary_role_options_remain_unfiltered_for_list_filters()
    {
        await using var db = CreateDb();
        var allowed = CreateRole("Allowed role", [IdentityPermissions.Roles.View]);
        var elevated = CreateRole("Elevated role", [IdentityPermissions.Users.Update]);
        db.Roles.AddRange(allowed, elevated);
        await db.SaveChangesAsync();

        var handler = new GetRoleOptionsQueryHandler(db, new TestUserContext([]));
        var result = await handler.Handle(
            new GetRoleOptionsQuery(UserType.SystemAdministrator),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(option => option.Id).ShouldBe([allowed.Id, elevated.Id], ignoreOrder: true);
    }

    private static IdentityDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"identity-role-options-{Guid.NewGuid():N}")
            .Options);

    private static Role CreateRole(string name, IReadOnlyList<string> permissions) =>
        Role.Create(
            name,
            description: null,
            permissions,
            UserType.SystemAdministrator,
            DateTimeOffset.UtcNow).Value;

    private sealed class TestUserContext(IReadOnlyList<string> permissions) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => permissions.Contains(permission);
    }
}
