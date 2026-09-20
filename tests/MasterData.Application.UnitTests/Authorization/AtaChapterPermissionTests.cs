using BuildingBlocks.Contracts.Authorization;
using MasterData.Application.Authorization;
using Shouldly;

namespace MasterData.Application.UnitTests.Authorization;

public sealed class AtaChapterPermissionTests
{
    [Fact]
    public void Ata_catalog_mutations_are_admin_only_and_views_allow_station_staff()
    {
        var permissions = new MasterDataPermissionCatalog().Permissions
            .Where(x => x.Code.StartsWith("masterdata.ata-chapter", StringComparison.Ordinal)).ToList();
        permissions.Count.ShouldBe(10);
        foreach (var permission in permissions)
        {
            permission.IsCompatibleWith(UserType.SystemAdministrator).ShouldBeTrue();
            permission.IsCompatibleWith(UserType.StationStaff).ShouldBe(permission.Code.EndsWith(".view", StringComparison.Ordinal));
            permission.IsCompatibleWith(UserType.CustomerContact).ShouldBeFalse();
            permission.GrantsPortalPage.ShouldBe(permission.Code.EndsWith(".view", StringComparison.Ordinal));
        }
    }
}
