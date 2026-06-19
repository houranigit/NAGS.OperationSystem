using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Shouldly;

namespace Identity.Domain.UnitTests.Roles;

public class RoleTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_with_unknown_permission_fails()
    {
        var result = Role.Create("Ops", null, ["not.a.real.permission"], Now);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Create_dedupes_permissions_and_normalizes_name()
    {
        var result = Role.Create("  Dispatcher  ", "desc",
            [IdentityPermissions.Users.View, IdentityPermissions.Users.View], Now);

        result.IsSuccess.ShouldBeTrue();
        var role = result.Value;
        role.Name.ShouldBe("Dispatcher");
        role.NormalizedName.ShouldBe("DISPATCHER");
        role.Permissions.Count.ShouldBe(1);
    }

    [Fact]
    public void SetPermissions_replaces_and_validates()
    {
        var role = Role.Create("Ops", null, [IdentityPermissions.Users.View], Now).Value;

        role.SetPermissions([IdentityPermissions.Roles.View, IdentityPermissions.Roles.Create], Now);

        role.HasPermission(IdentityPermissions.Roles.View).ShouldBeTrue();
        role.HasPermission(IdentityPermissions.Users.View).ShouldBeFalse();
        role.Permissions.Count.ShouldBe(2);
    }

    [Fact]
    public void Create_empty_name_fails()
    {
        var result = Role.Create("  ", null, [], Now);

        result.IsFailure.ShouldBeTrue();
    }
}
