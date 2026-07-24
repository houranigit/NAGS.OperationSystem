using BuildingBlocks.Contracts.Authorization;
using Identity.Application.Features.Users;
using Shouldly;

namespace Identity.Application.UnitTests.Features.Users;

public sealed class PortalSourceTests
{
    [Theory]
    [InlineData(UserType.SystemAdministrator)]
    [InlineData(UserType.ViewerOnly)]
    public void Direct_account_types_report_direct_portal_source(UserType userType)
    {
        PortalSource.For(userType).ShouldBe("Direct");
    }

    [Theory]
    [InlineData(UserType.StationStaff)]
    [InlineData(UserType.CustomerContact)]
    public void Linked_account_types_report_master_data_portal_source(UserType userType)
    {
        PortalSource.For(userType).ShouldBe("MasterData");
    }
}
